using System.ComponentModel.DataAnnotations;

namespace Portion.Infrastructure.Configuration;

/// <summary>Strongly typed, validated binding of the <c>Ollama</c> configuration section.</summary>
public sealed class OllamaOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ollama";

    /// <summary>Base address of the local Ollama HTTP server.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Ollama:BaseUrl is required.")]
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Chat model tag, e.g. <c>qwen2.5:1.5b</c>.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Ollama:Model is required.")]
    public string Model { get; set; } = "qwen2.5:1.5b";

    /// <summary>Embedding model tag, e.g. <c>nomic-embed-text</c>.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Ollama:EmbeddingModel is required.")]
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>
    /// Per-request timeout in seconds. Zero disables the timeout, which is what streaming chat needs
    /// on a cold model load but is deliberately not the default for the rest of the surface.
    /// </summary>
    [Range(0, 3600, ErrorMessage = "Ollama:TimeoutSeconds must be between 0 and 3600.")]
    public int TimeoutSeconds { get; set; }

    /// <summary>Absolute base URL with any trailing slash removed.</summary>
    public Uri BaseUri => new(BaseUrl.TrimEnd('/'), UriKind.Absolute);
}
