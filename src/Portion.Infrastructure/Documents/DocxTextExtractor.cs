using DocumentFormat.OpenXml.Packaging;
using Portion.Application.Abstractions;

namespace Portion.Infrastructure.Documents;

/// <summary>Extracts text from Open XML word-processing documents using the Open XML SDK.</summary>
public sealed class DocxTextExtractor : IDocumentTextExtractor
{
    /// <summary>The extensions this extractor claims.</summary>
    public static readonly string[] Extensions = [".docx"];

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    /// <inheritdoc />
    public Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        using var document = WordprocessingDocument.Open(filePath, isEditable: false);

        return Task.FromResult(document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty);
    }
}
