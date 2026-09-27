using Portion.Application.Contracts;

namespace Portion.Application.Services;

/// <summary>
/// Builds the prompts sent to the local chat model. Kept as a separate, unit-testable type so the
/// grounding contract can be asserted without touching a database or an HTTP client.
/// </summary>
public static class PromptBuilder
{
    /// <summary>Verbatim grounding contract for the screening assistant.</summary>
    public const string SystemPrompt =
        "You are Portion AI, an elite enterprise recruitment screening agent. Analyze the provided " +
        "candidate resume context to answer the HR recruiter's question. Only use factual data from " +
        "the provided text. If information is missing, clearly state it is not specified. Respond " +
        "using clean, professional markdown with headings, bold text, bullet lists, or tables where applicable.";

    /// <summary>Grounding text used when the resume corpus holds no indexed content at all.</summary>
    public const string EmptyCorpusNotice =
        "No candidate resumes are currently indexed in the system.";

    /// <summary>Label prefixing each grounding chunk with its candidate.</summary>
    public const string CandidateAttributionFormat = "[Candidate: {0}]";

    /// <summary>Separator placed between grounding chunks.</summary>
    public const string ChunkSeparator = "\n\n---\n\n";

    /// <summary>Builds the user turn containing the grounded context and the recruiter's question.</summary>
    public static string BuildUserPrompt(string groundedContext, string recruiterQuery)
    {
        ArgumentNullException.ThrowIfNull(groundedContext);
        ArgumentNullException.ThrowIfNull(recruiterQuery);

        return $"Context Information:\n{groundedContext}\n\nRecruiter Query: {recruiterQuery}";
    }

    /// <summary>Formats a single grounding chunk with candidate attribution.</summary>
    public static string FormatChunk(string candidateName, string chunkText) =>
        string.Format(CandidateAttributionFormat, candidateName) + "\n" + chunkText;

    /// <summary>Joins grounding chunks with the document separator.</summary>
    public static string JoinChunks(IEnumerable<string> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        return string.Join(ChunkSeparator, chunks);
    }

    /// <summary>
    /// Applies the empty-corpus rule: any grounded text, or the literal notice when the corpus is
    /// empty. Guarantees the model is never given an empty context block.
    /// </summary>
    public static string ResolveContext(string? groundedContext) =>
        string.IsNullOrWhiteSpace(groundedContext) ? EmptyCorpusNotice : groundedContext;
}
