namespace Portion.Application.Abstractions;

/// <summary>Generates embedding vectors for text.</summary>
public interface IEmbeddingGenerator
{
    /// <summary>
    /// Produces an embedding vector, or <c>null</c> when the backing model is unavailable. Returning
    /// <c>null</c> is not an error: the ingestion pipeline persists the chunk without a vector and
    /// retrieval degrades to keyword matching.
    /// </summary>
    Task<float[]?> GenerateAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>Streams chat completions token by token.</summary>
public interface IChatCompletionStreamer
{
    /// <summary>
    /// Yields content deltas for the supplied conversation. A non-fatal downstream fault should be
    /// surfaced either as a thrown exception or as a single descriptive delta, never as a hang.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
