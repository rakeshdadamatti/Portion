using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Portion.Domain.Entities;

namespace Portion.Application.Abstractions;

/// <summary>Identifies which relational provider backs the current database.</summary>
public enum DatabaseProviderKind
{
    /// <summary>SQLite. Vectors are stored as JSON <c>float[]</c> and compared in memory.</summary>
    Sqlite = 0,

    /// <summary>PostgreSQL with pgvector. Vectors are stored natively as <c>vector(768)</c>.</summary>
    Postgres = 1
}

/// <summary>
/// The persistence surface visible to the application layer. Exposed in place of
/// <c>DbContext</c> so that feature code cannot reach provider-specific APIs.
/// </summary>
public interface IApplicationDbContext
{
    /// <summary>Resume rows.</summary>
    DbSet<Resume> Resumes { get; }

    /// <summary>Resume chunk rows.</summary>
    DbSet<ResumeChunk> ResumeChunks { get; }

    /// <summary>The active provider. Drives vector-search strategy selection.</summary>
    DatabaseProviderKind ProviderKind { get; }

    /// <summary>
    /// The change tracker, exposed so a partially-applied unit of work can be discarded after a
    /// failure. Without this, pending inserts made before the fault would be flushed by the
    /// subsequent <c>SaveChangesAsync</c> that records the failure.
    /// </summary>
    ChangeTracker ChangeTracker { get; }

    /// <summary>Flushes pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Transaction boundary over <see cref="IApplicationDbContext" />.</summary>
public interface IUnitOfWork
{
    /// <summary>The underlying context.</summary>
    IApplicationDbContext DbContext { get; }

    /// <summary>Flushes pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
