using System.ComponentModel.DataAnnotations;

namespace Portion.Infrastructure.Configuration;

/// <summary>Strongly typed, validated binding of the <c>Ingestion</c> configuration section.</summary>
public sealed class IngestionOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ingestion";

    /// <summary>Chunk width, in words.</summary>
    [Range(1, 10_000, ErrorMessage = "Ingestion:ChunkSize must be between 1 and 10000.")]
    public int ChunkSize { get; set; } = 500;

    /// <summary>Word overlap between adjacent chunks.</summary>
    [Range(0, 9_999, ErrorMessage = "Ingestion:ChunkOverlap must be between 0 and 9999.")]
    public int ChunkOverlap { get; set; } = 50;

    /// <summary>Extensions accepted for ingestion, dot-prefixed.</summary>
    [MinLength(1, ErrorMessage = "Ingestion:SupportedExtensions must list at least one extension.")]
    public string[] SupportedExtensions { get; set; } = [".pdf", ".docx", ".txt"];

    /// <summary>Default number of grounding chunks returned to the screening prompt.</summary>
    [Range(1, 100, ErrorMessage = "Ingestion:TopChunkResults must be between 1 and 100.")]
    public int TopChunkResults { get; set; } = 5;

    /// <summary>Largest accepted upload, in bytes. Defaults to 25 MiB.</summary>
    [Range(1, long.MaxValue, ErrorMessage = "Ingestion:MaxUploadBytes must be greater than zero.")]
    public long MaxUploadBytes { get; set; } = 26_214_400;
}
