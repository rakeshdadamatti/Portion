using System.Text.Json.Serialization;
using Portion.Application.Abstractions;

namespace Portion.Application.Contracts;

/// <summary>Stages announced by the <c>status</c> SSE event, in emission order.</summary>
public enum ScreeningStage
{
    /// <summary>The recruiter question is being embedded.</summary>
    [JsonPropertyName("embedding")]
    Embedding,

    /// <summary>Vector similarity search is running.</summary>
    [JsonPropertyName("search")]
    Search,

    /// <summary>Vector search returned nothing; keyword or first-available retrieval is running.</summary>
    [JsonPropertyName("fallback")]
    Fallback,

    /// <summary>The model is producing the grounded answer.</summary>
    [JsonPropertyName("generating")]
    Generating,

    /// <summary>The stream finished successfully.</summary>
    [JsonPropertyName("complete")]
    Complete
}

/// <summary>A per-resume retrieval result for the <c>matches</c> SSE event.</summary>
/// <param name="ResumeId">Resume identity.</param>
/// <param name="CandidateName">Display name.</param>
/// <param name="Score">Similarity in [0, 1], rounded to four decimal places.</param>
/// <param name="ChunkCount">Number of grounding chunks attributed to this resume.</param>
public sealed record ResumeMatchCandidate(Guid ResumeId, string CandidateName, double Score, int ChunkCount);

/// <summary>Base type of every event emitted while answering a screening question.</summary>
public abstract record ScreeningEvent;

/// <summary>Announces a pipeline stage. Mapped to the SSE <c>status</c> event.</summary>
public sealed record ScreeningStatusEvent(ScreeningStage Stage) : ScreeningEvent;

/// <summary>Reports the retrieved grounding chunks. Mapped to the SSE <c>matches</c> event.</summary>
public sealed record ScreeningMatchesEvent(
    int TopChunkCount,
    IReadOnlyList<ResumeMatchCandidate> Candidates) : ScreeningEvent;

/// <summary>A single model output delta. Mapped to the SSE <c>token</c> event.</summary>
public sealed record ScreeningTokenEvent(string Token) : ScreeningEvent;

/// <summary>Terminal success event carrying round-trip timing. Mapped to the SSE <c>done</c> event.</summary>
public sealed record ScreeningDoneEvent(long ElapsedMs) : ScreeningEvent;

/// <summary>Terminal failure event. Mapped to the SSE <c>error</c> event.</summary>
public sealed record ScreeningErrorEvent(string Message) : ScreeningEvent;
