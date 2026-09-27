using Microsoft.EntityFrameworkCore;
using Portion.Application.Abstractions;
using Portion.Domain.Entities;
using Portion.Domain.Enums;

namespace Portion.Infrastructure.Repositories;

/// <summary>
/// Data access for resume rows.
/// </summary>
/// <remarks>
/// The read-heavy resume queries live in the feature handlers as <c>IQueryable</c> projections, which
/// is both the cleanest way to express "count via projection" and the only way to keep chunk counts out
/// of the table. This repository covers the row-level access that the handlers share: existence
/// checks, status transitions and hash lookups.
/// </remarks>
public sealed class ResumeRepository
{
    private readonly IApplicationDbContext _db;

    /// <summary>Creates the repository.</summary>
    public ResumeRepository(IApplicationDbContext db) =>
        _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <summary>Total number of tracked resumes.</summary>
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        _db.Resumes.CountAsync(cancellationToken);

    /// <summary>Finds a resume by its content hash.</summary>
    public Task<Resume?> FindByHashAsync(string fileHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileHash);

        return _db.Resumes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FileHash == fileHash, cancellationToken);
    }

    /// <summary>Finds a tracked resume.</summary>
    public Task<Resume?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Resumes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <summary>Transitions a resume to a new status, optionally recording a failure reason.</summary>
    public async Task<bool> TrySetStatusAsync(
        Guid id,
        IngestionStatus status,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        var resume = await _db.Resumes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

        if (resume is null)
        {
            return false;
        }

        resume.Status = status;
        resume.FailureReason = failureReason;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>Number of resumes in a given state.</summary>
    public Task<int> CountByStatusAsync(IngestionStatus status, CancellationToken cancellationToken = default) =>
        _db.Resumes.CountAsync(r => r.Status == status, cancellationToken);
}
