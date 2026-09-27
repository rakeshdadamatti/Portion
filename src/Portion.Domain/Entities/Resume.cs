using Portion.Domain.Enums;

namespace Portion.Domain.Entities;

/// <summary>
/// A candidate resume tracked by the system. Property names and types are kept byte-compatible with
/// the original hand-created SQLite schema (<c>Resumes</c> table) so existing developer databases
/// continue to open without a migration.
/// </summary>
public class Resume
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name, derived from the source file name on upload.</summary>
    public string CandidateName { get; set; } = string.Empty;

    /// <summary>Absolute path on disk holding the source document.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Upper-case hex SHA-256 of the source file; the deduplication key.</summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>Current pipeline state.</summary>
    public IngestionStatus Status { get; set; } = IngestionStatus.Discovered;

    /// <summary>Populated only when <see cref="Status" /> is <see cref="IngestionStatus.Failed" />.</summary>
    public string? FailureReason { get; set; }

    /// <summary>When the row was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When ingestion last completed successfully.</summary>
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Navigational collection of chunks. Cascade-deleted with the resume.</summary>
    public ICollection<ResumeChunk> Chunks { get; set; } = new List<ResumeChunk>();
}
