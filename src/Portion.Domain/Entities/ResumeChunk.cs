using Pgvector;

namespace Portion.Domain.Entities;

/// <summary>
/// A contiguous slice of a resume's extracted text plus its optional embedding vector.
/// Property names and types are kept byte-compatible with the original hand-created SQLite schema
/// (<c>ResumeChunks</c> table) so existing developer databases continue to open without a migration.
/// </summary>
public class ResumeChunk
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning resume.</summary>
    public Guid ResumeId { get; set; }

    /// <summary>Owning resume navigation.</summary>
    public Resume Resume { get; set; } = null!;

    /// <summary>The chunk's plain text.</summary>
    public string TextContent { get; set; } = string.Empty;

    /// <summary>
    /// Embedding vector. Mapped to a native <c>vector(768)</c> column on PostgreSQL and to a
    /// JSON-serialised <c>float[]</c> text column on SQLite.
    /// </summary>
    public Vector? Embedding { get; set; }

    /// <summary>Zero-based position of this chunk within its resume.</summary>
    public int ChunkIndex { get; set; }
}
