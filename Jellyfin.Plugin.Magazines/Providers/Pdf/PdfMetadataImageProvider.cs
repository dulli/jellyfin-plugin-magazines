using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Magazines.Providers.Pdf
{
    /// <summary>
    /// Pdf metadata image provider.
    /// </summary>
    public class PdfMetadataImageProvider : IDynamicImageProvider
    {
        private readonly ILogger<PdfMetadataImageProvider> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfMetadataImageProvider"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{PdfMetadataImageProvider}"/> interface.</param>
        public PdfMetadataImageProvider(ILogger<PdfMetadataImageProvider> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => "Pdf Metadata";

        /// <inheritdoc />
        public bool Supports(BaseItem item)
        {
            return item is Book;
        }

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
        }

        /// <inheritdoc />
        public Task<DynamicImageResponse> GetImage(BaseItem item, ImageType type, CancellationToken cancellationToken)
        {
            if (string.Equals(Path.GetExtension(item.Path), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return ReadPdfFile(item.Path);
            }

            return Task.FromResult(new DynamicImageResponse { HasImage = false });
        }

        private async Task<DynamicImageResponse> LoadCover(XmlDocument xmp)
        {
            var utilities = new XmpReader<PdfMetadataImageProvider>(xmp, _logger);
            var coverRef = utilities.ReadCoverData();
            if (coverRef == null)
            {
                return new DynamicImageResponse { HasImage = false };
            }

            var cover = coverRef.Value;
            var memoryStream = new MemoryStream();
            using (var coverStream = cover.Data)
            {
                await coverStream.CopyToAsync(memoryStream)
                    .ConfigureAwait(false);
            }

            memoryStream.Position = 0;

            var response = new DynamicImageResponse
            {
                HasImage = true,
                Stream = memoryStream
            };
            response.SetFormatFromMimeType(cover.MimeType);

            return response;
        }

        private async Task<DynamicImageResponse> ReadPdfFile(string path)
        {
            var xmpData = PdfUtils.ReadXmpData(path);
            if (xmpData == null)
            {
                return new DynamicImageResponse { HasImage = false };
            }

            var xmpDocument = new XmlDocument();
            xmpDocument.LoadXml(xmpData);
            return await LoadCover(xmpDocument).ConfigureAwait(false);
        }
    }
}
