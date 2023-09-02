using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Magazines.Providers
{
    /// <summary>
    /// XMP reader.
    /// </summary>
    /// <typeparam name="TCategoryName">The type of category.</typeparam>
    public class XmpReader<TCategoryName>
    {
        private const string RdfNamespace = @"http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        private const string DcNamespace = @"http://purl.org/dc/elements/1.1/";
        private const string PdfNamespace = @"http://ns.adobe.com/pdf/1.3/";
        private const string XmpNamespace = @"http://ns.adobe.com/xap/1.0/";
        private const string XmpGImgNamespace = @"http://ns.adobe.com/xap/1.0/g/img/";
        private const string CalibreNamespace = @"http://calibre-ebook.com/xmp-namespace";
        private const string CalibreSiNamespace = @"http://calibre-ebook.com/xmp-namespace-series-index";

        private readonly XmlNamespaceManager _namespaceManager;

        private readonly XmlDocument _document;

        private readonly ILogger<TCategoryName> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="XmpReader{TCategoryName}"/> class.
        /// </summary>
        /// <param name="doc">The xdocument to parse.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
        public XmpReader(XmlDocument doc, ILogger<TCategoryName> logger)
        {
            _document = doc;
            _logger = logger;
            _namespaceManager = new XmlNamespaceManager(_document.NameTable);
            _namespaceManager.AddNamespace("rdf", RdfNamespace);
            _namespaceManager.AddNamespace("dc", DcNamespace);
            _namespaceManager.AddNamespace("pdf", PdfNamespace);
            _namespaceManager.AddNamespace("xmp", XmpNamespace);
            _namespaceManager.AddNamespace("xmpGImg", XmpGImgNamespace);
            _namespaceManager.AddNamespace("calibre", CalibreNamespace);
            _namespaceManager.AddNamespace("calibreSI", CalibreSiNamespace);
        }

        /// <summary>
        /// Checks the pdf for the existence of a cover.
        /// </summary>
        /// <returns>Returns the found cover data and it's type or null.</returns>
        public (string MimeType, MemoryStream Data)? ReadCoverData()
        {
            string coverData = string.Empty;
            string mediaType = string.Empty;
            ReadStringInto("//xmp:Thumbnails//xmpGImg:image", data => coverData = data);
            ReadStringInto("//xmp:Thumbnails//xmpGImg:format", format => mediaType = format);

            mediaType = "image/" + mediaType.ToLower(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(coverData)
                            && !string.IsNullOrEmpty(mediaType) && IsValidImage(mediaType))
            {
                return (MimeType: mediaType, Data: new MemoryStream(Convert.FromBase64String(coverData)));
            }

            return null;
        }

        /// <summary>
        /// Read xmp data.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The metadata result to update.</returns>
        public MetadataResult<Book> ReadXmpData(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var book = CreateBookFromXmp();
            var bookResult = new MetadataResult<Book> { Item = book, HasMetadata = true };
            ReadStringInto("//dc:creator//rdf:li", author =>
            {
                var person = new PersonInfo { Name = author, Type = "Author" };
                bookResult.AddPerson(person);
            });

            ReadStringInto("//dc:language//rdf:li", language => bookResult.ResultLanguage = language);

            return bookResult;
        }

        private Book CreateBookFromXmp()
        {
            var book = new Book();

            book.Name = FindMainTitle();
            book.ForcedSortName = FindSortTitle();

            ReadStringInto("//dc:description//rdf:li", summary => book.Overview = summary);
            ReadStringInto("//dc:publisher//rdf:li", publisher => book.AddStudio(publisher));
            // ReadStringInto("//dc:identifier[@opf:scheme='ISBN']", isbn => book.SetProviderId("ISBN", isbn));
            // ReadStringInto("//dc:identifier[@opf:scheme='AMAZON']", amazon => book.SetProviderId("Amazon", amazon));

            ReadStringInto("//dc:date//rdf:li", date =>
            {
                if (DateTime.TryParse(date, out var dateValue))
                {
                    book.PremiereDate = dateValue.Date;
                    book.ProductionYear = dateValue.Date.Year;
                }
            });

            // var genresNodes = _document.SelectNodes("//dc:subject//rdf:li", _namespaceManager);

            // if (genresNodes != null && genresNodes.Count > 0)
            // {
            //     foreach (var node in genresNodes.Cast<XmlNode>().Where(node => !book.Tags.Contains(node.InnerText)))
            //     {
            //         if (node.InnerText != null)
            //         {
            //             // Adding to tags because we can't be sure the values are all genres
            //             book.AddGenre(node.InnerText);
            //         }
            //     }
            // }

            ReadStringInto("//calibre:series/rdf:value", series => book.SeriesName = series);
            ReadInt32AttributeInto("//calibreSI:series_index", index => book.IndexNumber = index);
            ReadInt32AttributeInto("//calibre:rating", rating => book.CommunityRating = rating);

            return book;
        }

        private string FindMainTitle()
        {
            string title = string.Empty;
            ReadStringInto("//dc:title", titleStr => title = titleStr);
            return title;
        }

        private string? FindSortTitle()
        {
            string titleSort = string.Empty;
            ReadStringInto("//dc:title", titleStr => titleSort = titleStr);
            return titleSort;
        }

        private void ReadStringInto(string xPath, Action<string> commitResult)
        {
            var resultElement = _document.SelectSingleNode(xPath, _namespaceManager);
            if (resultElement is not null && !string.IsNullOrWhiteSpace(resultElement.InnerText))
            {
                commitResult(resultElement.InnerText);
            }
        }

        private void ReadInt32AttributeInto(string xPath, Action<int> commitResult)
        {
            var resultElement = _document.SelectSingleNode(xPath, _namespaceManager);
            var resultValue = resultElement?.Attributes?["content"]?.Value;
            if (!string.IsNullOrEmpty(resultValue))
            {
                try
                {
                    commitResult(Convert.ToInt32(resultValue, CultureInfo.InvariantCulture));
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error converting to int32");
                }
            }
        }

        private bool IsValidImage(string? mimeType)
        {
            return !string.IsNullOrEmpty(mimeType)
                   && !string.IsNullOrWhiteSpace(MimeTypes.ToExtension(mimeType));
        }
    }
}
