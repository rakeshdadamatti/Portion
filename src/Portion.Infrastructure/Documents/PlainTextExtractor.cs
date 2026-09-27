using System.Text;
using Portion.Application.Abstractions;

namespace Portion.Infrastructure.Documents;

/// <summary>Extracts text from plain-text and Markdown files.</summary>
public sealed class PlainTextExtractor : IDocumentTextExtractor
{
    /// <summary>The extensions this extractor claims.</summary>
    public static readonly string[] Extensions = [".txt", ".md", ".text", ".csv", ".json"];

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    /// <inheritdoc />
    public async Task<string> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }
}
