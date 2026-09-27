using Portion.Application.Contracts;

namespace Portion.Application.Abstractions;

/// <summary>Which retrieval strategy produced the context chunks for a screening question.</summary>
public enum RetrievalMode
{
    /// <summary>Nothing was retrieved; the corpus is empty.</summary>
    None = 0,

    /// <summary>Vector similarity search succeeded.</summary>
    Vector = 1,

    /// <summary>Vector search returned nothing; a SQL <c>LIKE</c> keyword search was used.</summary>
    Keyword = 2,

    /// <summary>Vector and keyword search returned nothing; the first chunks were used.</summary>
    FirstAvailable = 3
}

/// <summary>Parameters for a single retrieval pass.</summary>
/// <param name="Query">The recruiter's question, used verbatim for the keyword fallback.</param>
/// <param name="TopK">Maximum number of chunks to return.</param>
/// <param name="QueryEmbedding">
/// Pre-computed embedding of <paramref name="Query" />, or <c>null</c> when the embedding model is
/// unavailable. Supplying it lets the caller announce the "embedding" and "search" SSE stages around
/// the call without the search service needing an AI client of its own.
/// </param>
public sealed record ScreeningRequest(string Query, int TopK, float[]? QueryEmbedding = null);

/// <summary>Resolves the grounded context for a recruiter question.</summary>
public interface IResumeSearchService
{
    /// <summary>
    /// Runs the retrieval cascade: vector similarity, then keyword <c>LIKE</c>, then the first
    /// available chunks. Never throws for an empty corpus — it returns an empty result with
    /// <see cref="RetrievalMode.None" />.
    /// </summary>
    Task<RetrievalResult> RetrieveAsync(
        ScreeningRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>A chunk selected for grounding, with its similarity score.</summary>
/// <param name="ResumeId">Owning resume.</param>
/// <param name="CandidateName">Owning candidate's display name.</param>
/// <param name="ChunkId">Chunk identity.</param>
/// <param name="Text">Chunk text.</param>
/// <param name="Score">Similarity in [0, 1], already rounded to four decimal places.</param>
public sealed record ScoredChunk(
    Guid ResumeId,
    string CandidateName,
    Guid ChunkId,
    string Text,
    double Score);

/// <summary>Outcome of a retrieval cascade.</summary>
public sealed record RetrievalResult(
    IReadOnlyList<ScoredChunk> Chunks,
    RetrievalMode Mode)
{
    /// <summary>True when the corpus yielded no grounded context at all.</summary>
    public bool IsEmpty => Chunks.Count == 0;

    /// <summary>Per-resume roll-up, highest score first, for the SSE <c>matches</c> event.</summary>
    public IReadOnlyList<ResumeMatchCandidate> ToCandidates() =>
        Chunks
            .GroupBy(c => c.ResumeId)
            .Select(g => new ResumeMatchCandidate(
                g.Key,
                g.First().CandidateName,
                g.Max(c => c.Score),
                g.Count()))
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.CandidateName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Ground text handed to the model, using the "no resumes" notice when empty.</summary>
    public string ToGroundedContext() =>
        Chunks.Count == 0
            ? Services.PromptBuilder.EmptyCorpusNotice
            : string.Join(
                Services.PromptBuilder.ChunkSeparator,
                Chunks.Select(c => Services.PromptBuilder.FormatChunk(c.CandidateName, c.Text)));
}

/// <summary>
/// Vector nearest-neighbour lookup. Implemented in Infrastructure so that the
/// <c>Pgvector.EntityFrameworkCore</c> <c>CosineDistance</c> translation never leaks into the
/// application layer.
/// </summary>
public interface IVectorChunkSearch
{
    /// <summary>
    /// Returns the <paramref name="topK" /> most similar embedded chunks, highest score first.
    /// Returns an empty list when the query has no usable embedding.
    /// </summary>
    Task<IReadOnlyList<ScoredChunk>> FindNearestAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default);
}

/// <summary>Executes the streaming screening conversation.</summary>
public interface IScreeningService
{
    /// <summary>
    /// Emits an ordered stream of typed events: status, per-resume matches, model output tokens, and
    /// a terminal done event. Honours <paramref name="cancellationToken" /> throughout.
    /// </summary>
    IAsyncEnumerable<ScreeningEvent> ExecuteAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default);
}
