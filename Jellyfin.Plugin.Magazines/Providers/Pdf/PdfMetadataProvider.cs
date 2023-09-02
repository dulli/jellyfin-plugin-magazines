using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Magazines.Providers.Pdf
{
    /// <summary>
    /// Pdf metadata provider.
    /// </summary>
    public class PdfMetadataProvider : ILocalMetadataProvider<Book>
    {
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<PdfMetadataProvider> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfMetadataProvider"/> class.
        /// </summary>
        /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{PdfMetadataProvider}"/> interface.</param>
        public PdfMetadataProvider(IFileSystem fileSystem, ILogger<PdfMetadataProvider> logger)
        {
            _fileSystem = fileSystem;
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => "Pdf Metadata";

        /// <inheritdoc />
        public Task<MetadataResult<Book>> GetMetadata(
            ItemInfo info,
            IDirectoryService directoryService,
            CancellationToken cancellationToken)
        {
            var path = GetPdfFile(info.Path)?.FullName;

            if (path is null)
            {
                return Task.FromResult(new MetadataResult<Book> { HasMetadata = false });
            }

            var result = ReadPdfFile(path, cancellationToken);

            if (result is null)
            {
                return Task.FromResult(new MetadataResult<Book> { HasMetadata = false });
            }
            else
            {
                return Task.FromResult(result);
            }
        }

        private FileSystemMetadata? GetPdfFile(string path)
        {
            var fileInfo = _fileSystem.GetFileSystemInfo(path);

            if (fileInfo.IsDirectory)
            {
                return null;
            }

            if (!string.Equals(Path.GetExtension(fileInfo.FullName), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return fileInfo;
        }

        private MetadataResult<Book>? ReadPdfFile(string path, CancellationToken cancellationToken)
        {
            var xmpData = PdfUtils.ReadXmpData(path);
            if (xmpData == null)
            {
                return null;
            }

            var xmpDocument = new XmlDocument();
            xmpDocument.LoadXml(xmpData);

            var utilities = new XmpReader<PdfMetadataProvider>(xmpDocument, _logger);
            return utilities.ReadXmpData(cancellationToken);
        }
    }
}
