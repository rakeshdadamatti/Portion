using System.ComponentModel.DataAnnotations;

namespace Portion.Infrastructure.Configuration;

/// <summary>Strongly typed, validated binding of the <c>Storage</c> configuration section.</summary>
public sealed class StorageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// Directory that owns managed uploads, resolved against <see cref="AppDataRoot" />.
    /// Deliberately moved out of the ingestion section: it is a storage concern, not a pipeline one.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Storage:UploadDirectory is required.")]
    public string UploadDirectory { get; set; } = "UploadedResumes";

    /// <summary>
    /// Optional explicit base directory for every relative path the application resolves at runtime
    /// (the upload directory and a relative SQLite <c>Data Source</c>). When left blank the base
    /// directory is discovered, which lets the API be launched from the repository root, from a
    /// project directory, or from a published output folder without reconfiguration.
    /// </summary>
    public string? AppDataRoot { get; set; }
}
