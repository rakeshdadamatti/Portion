namespace Portion.Application.Abstractions;

/// <summary>Splits extracted document text into overlapping, word-aligned chunks.</summary>
public interface ITextChunker
{
    /// <summary>
    /// Chunks <paramref name="text" /> into windows of <paramref name="chunkSize" /> words advancing by
    /// <c>max(chunkSize - overlap, 1)</c> words. Non-empty text always yields at least one chunk.
    /// </summary>
    IReadOnlyList<string> Chunk(string text, int chunkSize, int overlap);
}

/// <summary>Extracts plain text from one document format.</summary>
public interface IDocumentTextExtractor
{
    /// <summary>The file extensions this extractor handles, lower-case and dot-prefixed.</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>Extracts the document's plain text. Throws when the file cannot be read.</summary>
    Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>Selects the correct <see cref="IDocumentTextExtractor" /> for a file.</summary>
public interface IDocumentTextExtractorFactory
{
    /// <summary>
    /// Returns the extractor registered for the file's extension. Throws
    /// <see cref="UnsupportedDocumentException" /> when the extension is not supported.
    /// </summary>
    IDocumentTextExtractor Create(string filePath);
}

/// <summary>Raised when no extractor is registered for a document extension.</summary>
public sealed class UnsupportedDocumentException : Exception
{
    /// <summary>Creates the exception.</summary>
    public UnsupportedDocumentException(string extension, IReadOnlyCollection<string> supported)
        : base($"Unsupported document type '{extension}'. Supported types: {string.Join(", ", supported)}.")
    {
        Extension = extension;
        SupportedExtensions = supported;
    }

    /// <summary>The unsupported extension, including the leading dot.</summary>
    public string Extension { get; }

    /// <summary>Extensions that are supported.</summary>
    public IReadOnlyCollection<string> SupportedExtensions { get; }
}
