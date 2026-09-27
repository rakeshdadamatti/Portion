using Portion.Application.Abstractions;
using Portion.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Portion.Infrastructure.Documents;

/// <summary>
/// Strategy-pattern factory: resolves the <see cref="IDocumentTextExtractor" /> registered for a
/// file's extension, replacing the previous static type switch.
/// </summary>
/// <remarks>
/// Registration is dictionary-based and built once from the configured
/// <see cref="IngestionOptions.SupportedExtensions" />, so adding a format is a matter of registering
/// another extractor rather than editing a switch statement. Extensions that no extractor claims are
/// rejected up front with <see cref="UnsupportedDocumentException" /> rather than being silently read
/// as text.
/// </remarks>
public sealed class DocumentTextExtractorFactory : IDocumentTextExtractorFactory
{
    private readonly IReadOnlyDictionary<string, IDocumentTextExtractor> _extractors;
    private readonly IReadOnlyCollection<string> _supportedExtensions;

    /// <summary>Creates the factory from the registered extractors.</summary>
    public DocumentTextExtractorFactory(
        IEnumerable<IDocumentTextExtractor> extractors,
        IOptions<IngestionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(extractors);
        ArgumentNullException.ThrowIfNull(options);

        var map = new Dictionary<string, IDocumentTextExtractor>(StringComparer.OrdinalIgnoreCase);

        foreach (var extractor in extractors)
        {
            foreach (var extension in extractor.SupportedExtensions)
            {
                map[extension] = extractor;
            }
        }

        _extractors = map;

        // Only advertise extensions that actually have an extractor behind them, so an unsupported
        // type is rejected at upload time rather than failing later during ingestion.
        _supportedExtensions = options.Value.SupportedExtensions
            .Where(map.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Extensions that can be ingested with the current configuration and registrations.</summary>
    public IReadOnlyCollection<string> SupportedExtensions => _supportedExtensions;

    /// <inheritdoc />
    public IDocumentTextExtractor Create(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = Path.GetExtension(filePath);

        if (!string.IsNullOrEmpty(extension) && _extractors.TryGetValue(extension, out var extractor))
        {
            return extractor;
        }

        throw new UnsupportedDocumentException(
            string.IsNullOrEmpty(extension) ? "(none)" : extension,
            _supportedExtensions);
    }
}
