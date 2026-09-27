using System.ComponentModel.DataAnnotations;

namespace Portion.Api.Configuration;

/// <summary>Strongly typed, validated binding of the <c>Screening</c> configuration section.</summary>
public sealed class ScreeningOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Screening";

    /// <summary>Longest accepted recruiter question, in characters.</summary>
    [Range(1, 100_000, ErrorMessage = "Screening:MaxPromptLength must be between 1 and 100000.")]
    public int MaxPromptLength { get; set; } = 8_000;

    /// <summary>Number of grounding chunks used when the caller does not supply <c>topK</c>.</summary>
    [Range(1, 100, ErrorMessage = "Screening:DefaultTopK must be between 1 and 100.")]
    public int DefaultTopK { get; set; } = 5;

    /// <summary>Largest accepted <c>topK</c>.</summary>
    [Range(1, 100, ErrorMessage = "Screening:MaxTopK must be between 1 and 100.")]
    public int MaxTopK { get; set; } = 50;

    /// <summary>Minimum number of characters a question must contain after trimming.</summary>
    [Range(1, 1_000, ErrorMessage = "Screening:MinPromptLength must be between 1 and 1000.")]
    public int MinPromptLength { get; set; } = 1;
}
