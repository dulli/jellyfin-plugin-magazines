using System.IO;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Magazines.Providers.Pdf
{
    /// <summary>
    /// Pdf utils.
    /// </summary>
    public static class PdfUtils
    {
        /// <summary>
        /// Attempt to read xmp data from pdf file.
        /// </summary>
        /// <param name="pdfPath">The pdf's filepath.</param>
        /// <returns>The content file path.</returns>
        public static string? ReadXmpData(string pdfPath)
        {
            var pdfData = File.ReadAllText(pdfPath);
            if (pdfData == null)
            {
                return null;
            }

            string pattern = @"<<.*?/Metadata.*?(<x:xmpmeta.*?</x:xmpmeta>)";
            RegexOptions options = RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.RightToLeft;
            Match xmpData = Regex.Match(pdfData, pattern, options);
            if (!xmpData.Success)
            {
                return null;
            }

            return xmpData.Groups[1].ToString();
        }
    }
}
