using Portion.Application.Abstractions;

namespace Portion.Application.Services;

/// <summary>
/// Word-based sliding-window chunker.
/// </summary>
/// <remarks>
/// Semantics are intentionally stable: text is split on whitespace, windows are
/// <c>chunkSize</c> words wide and advance by <c>max(chunkSize - overlap, 1)</c> words, and the loop
/// stops as soon as the current window reaches the end of the document. Any non-empty input yields
/// at least one chunk.
/// </remarks>
public sealed class TextChunker : ITextChunker
{
    private static readonly char[] WordSeparators = [' ', '\r', '\n', '\t'];

    /// <inheritdoc />
    public IReadOnlyList<string> Chunk(string text, int chunkSize, int overlap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);

        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        var words = text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>(capacity: Math.Max(words.Length / Math.Max(chunkSize - overlap, 1), 1));
        var step = Math.Max(chunkSize - overlap, 1);

        for (var i = 0; i < words.Length; i += step)
        {
            chunks.Add(string.Join(' ', words.Skip(i).Take(chunkSize)));

            if (i + chunkSize >= words.Length)
            {
                break;
            }
        }

        // Single-chunk fallback for text that contains no split-able words (e.g. a bare token).
        if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            chunks.Add(text);
        }

        return chunks;
    }
}
