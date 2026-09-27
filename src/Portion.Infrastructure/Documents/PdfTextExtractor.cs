using System.Text;
using Portion.Application.Abstractions;
using UglyToad.PdfPig;

namespace Portion.Infrastructure.Documents;

/// <summary>Extracts text from PDF documents using PdfPig.</summary>
public sealed class PdfTextExtractor : IDocumentTextExtractor
{
    /// <summary>The extensions this extractor claims.</summary>
    public static readonly string[] Extensions = [".pdf"];

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    /// <inheritdoc />
    public Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Extract(filePath));
    }

    private static string Extract(string filePath)
    {
        // PdfPig parses the whole document eagerly and exposes no cancellable read loop, so
        // cancellation is observed by the caller between documents rather than between pages.
        // Malformed or encrypted PDFs throw, and the ingestion processor records the failure.
        using var document = PdfDocument.Open(filePath);
        var builder = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        return builder.ToString();
    }
}
