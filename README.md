<h1 align="center">Jellyfin Magazines Plugin</h1>

## About

The Jellyfin Magazines plugin enables the collection of magazines that can be read in Jellyfin (using the *Book* mediatype).
Ultimately it would make sense to integrate this into the [Bookshelf](https://github.com/jellyfin/jellyfin-plugin-bookshelf) plugin, as it is basically just an XMP reader that reads the fields used by Calibre when embedding metadata into a PDF file, so it can be used for all types of book-like files.

### Supported file types

- pdf

### Offline Metadata providers

This plugin supports the following offline Metadata providers. These will check the local files for metadata.

- [Extensible Metadata Platform (XMP)](https://developer.adobe.com/xmp/docs/)

### PDF Tagging

Use `scripts/xmptag.py` to embed metadata into your PDFs. This script simply parses the filepath (which should be in the format `{magazine_title}/{year}-{issue} - {issue-title}.pdf`) and renders the first page of the PDF as cover art.

## Build & Installation Process

1. Clone this repository

2. Ensure you have .NET Core SDK setup and installed

3. Build the plugin with following command:

```bash
dotnet publish --configuration Release --output bin
```

4. Place the resulting `Jellyfin.Plugin.Magazines.dll` file in a folder called `plugins/` inside your Jellyfin installation / data directory.
