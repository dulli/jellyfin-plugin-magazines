import glob
import base64
from io import BytesIO
from itertools import repeat
from PIL import Image
import pypdfium2 as pdfium
from pikepdf import Pdf, Stream
import shutil
import re
import xml.etree.ElementTree as ET

ENCODINGS = ("8", "16-le", "16-be", "32-le", "32-be")  # utf-
DEF_RE = r"(?P<magazine>[^\/]*)\/(?P<year>\d{4})-?(?P<issue>[\d-]*)? - (?P<title>.*).pdf$"
IMG_TYPE = "JPEG"
NS_MAP = {
    "dc": "http://purl.org/dc/elements/1.1/",
    "rdf": "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
    "pdf": "http://ns.adobe.com/pdf/1.3/",
    "xmp": "http://ns.adobe.com/xap/1.0/",
    "xmpGImg": "http://ns.adobe.com/xap/1.0/g/img/",
    "calibre": "http://calibre-ebook.com/xmp-namespace",
    "calibreSI": "http://calibre-ebook.com/xmp-namespace-series-index",
}
rdf_new = ET.Element("rdf:Description", attrib={"rdf:about": ""} | {f'xmlns:{k}': v for k, v in NS_MAP.items()})


magazine_tags = {
    "Playboy (Deutschland)": 'Men, Adult',
    "Der Spiegel": 'Economic, Business, Policy, News, Weekly',
    "Jolie": 'Women, Fashion',
    "InStyle": 'Women, Fashion',
}

def get_xmp(path: str) -> ET.ElementTree:
    # with Pdf.open(path) as pdf:
    #     xmp_data =  pdf.Root.Metadata.read_bytes()
    with open(path, "rb") as f:
        data_bytes = f.read()
        xmp_start = data_bytes.rfind(b"<x:xmpmeta")
        xmp_end = data_bytes.rfind(b"</x:xmpmeta>") + len(b"</x:xmpmeta>")
        xmp_data = data_bytes[xmp_start:xmp_end]
        xmp = BytesIO(xmp_data)

    # Parse the XML and register all namespaces it already contains
    namespaces = dict([node for _, node in ET.iterparse(xmp, events=["start-ns"])])
    xmp.seek(0)
    tree = ET.parse(xmp)
    for ns in namespaces:
        ET.register_namespace(ns, namespaces[ns])
    return tree


def set_xmp(path: str, xmp: ET.ElementTree, encoding="utf-8") -> bool:
    root_xmp = xmp.getroot()
    root_xmp.tail = "\n" + "\n".join(repeat(" " * 100, 30))
    data_xmp = ET.tostring(root_xmp)

    packet = f'<?xpacket begin="\ufeff" id="W5M0MpCehiHzreSzNTczkc9d"?>\n{data_xmp.decode("utf-8")}\n<?xpacket end="w"?>'

    path_tmp = path + ".tagged-output.pdf"
    with Pdf.open(path) as pdf:
        del pdf.docinfo
        pdf.Root.Metadata = Stream(pdf, packet.encode("utf-8"))
        pdf.save(path_tmp)
    shutil.move(path_tmp, path)


def build_xpath(node, path):  # see https://stackoverflow.com/a/5664332
    components = path.split("/")
    if components[0] == node.tag:
        components.pop(0)
    while components:
        # take in account positional  indexes in the form /path/para[3] or /path/para[location()=3]
        if "[" in components[0]:
            component, trail = components[0].split("[", 1)
            target_index = int(trail.split("=")[-1].strip("]"))
        else:
            component = components[0]
            target_index = 0
        components.pop(0)
        found_index = -1
        for child in list(node):
            if child.tag == component:
                found_index += 1
                if found_index == target_index:
                    node = child
                    break
        else:
            for i in range(target_index - found_index):
                new_node = ET.Element(component)
                node.append(new_node)
            node = new_node
    return node


def change_or_create(tree, xpath: str, text: str) -> ET.Element:
    node = tree.find(xpath, NS_MAP)
    if node is None:
        node = build_xpath(rdf_new, xpath.replace(".//", ""))
    node.text = text
    return node


def render(path: str, dpi=300, size=720) -> Image:
    pdf = pdfium.PdfDocument(path)
    page = pdf[0]
    bitmap = page.render(
        scale=dpi / 72,
        rotation=0,
    )
    pil_image = bitmap.to_pil()
    pil_image.thumbnail((size, size))
    return pil_image


def tag(path: str, lang: str = "de", regex=DEF_RE) -> ET.ElementTree:
    tree = get_xmp(path)

    # Extract Info from given path
    match = re.search(regex, path)
    magazine = match.group("magazine")
    title = f'{match.group("year")}-{match.group("issue")}'.strip("-")
    title = f'{title}: {match.group("title")}'
    year = match.group("year")
    issue = match.group("issue")
    index = f"{year}{issue.replace('-', '')}"


    tags = magazine_tags[magazine] if magazine in magazine_tags else ""
    # Add the Metadata and clear some fields
    change_or_create(tree, ".//dc:title/rdf:Alt/rdf:li", title)
    change_or_create(tree, ".//dc:description/rdf:Alt/rdf:li", "")
    change_or_create(tree, ".//dc:creator/rdf:Bag/rdf:li", "")
    change_or_create(tree, ".//dc:publisher/rdf:Bag/rdf:li", "")
    change_or_create(tree, ".//dc:subject/rdf:Bag/rdf:li", tags)
    change_or_create(tree, ".//dc:language/rdf:Bag/rdf:li", lang)
    change_or_create(tree, ".//calibre:series/rdf:value", magazine)
    change_or_create(tree, ".//calibre:series/calibreSI:series_index", index)
    change_or_create(tree, ".//pdf:Keywords", tags)

    # Get the Thumbnail
    buffered = BytesIO()
    thumb = render(path)
    thumb.save(buffered, format=IMG_TYPE, subsampling=0, quality=85)
    img_data = base64.b64encode(buffered.getvalue())
    img_width, img_height = thumb.size
    path_thumb = ".//xmp:Thumbnails/rdf:Alt/rdf:li"
    change_or_create(tree, f"{path_thumb}/xmpGImg:format", IMG_TYPE)
    change_or_create(tree, f"{path_thumb}/xmpGImg:width", str(img_width))
    change_or_create(tree, f"{path_thumb}/xmpGImg:height", str(img_height))
    change_or_create(tree, f"{path_thumb}/xmpGImg:image", img_data.decode("utf-8"))

    # Write the XML
    if list(rdf_new):
        ET.indent(rdf_new, level=1)
        rdf_root = tree.find(".//rdf:RDF", NS_MAP)
        rdf_root.append(rdf_new)
    set_xmp(path, tree)

    return tree

def insensitive_glob(pattern):
    def either(c):
        return '[%s%s]' % (c.lower(), c.upper()) if c.isalpha() else c
    return glob.glob(''.join(map(either, pattern)))

if __name__ == "__main__":
    for filepath in insensitive_glob("Test/*/*.pdf"):
        print(filepath)
        tree = tag(filepath, lang="de")

