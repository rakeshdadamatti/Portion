using Microsoft.EntityFrameworkCore;
using Portion.Application.Abstractions;
using Portion.Application.Contracts;
using Portion.Application.Services;
using Portion.Domain.Common;

namespace Portion.Application.Features.Resumes.Queries;

/// <summary>Fetches a single resume together with its ordered chunks.</summary>
/// <param name="Id">Resume identity.</param>
public sealed record GetResumeQuery(Guid Id);

/// <summary>Handles <see cref="GetResumeQuery" />.</summary>
public sealed class GetResumeQueryHandler
{
    private readonly IApplicationDbContext _db;

    /// <summary>Creates the handler.</summary>
    public GetResumeQueryHandler(IApplicationDbContext db) =>
        _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <summary>Executes the query, returning a not-found error for an unknown identifier.</summary>
    public async Task<Result<ResumeDetail>> HandleAsync(
        GetResumeQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var resume = await _db.Resumes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == query.Id, cancellationToken)
            .ConfigureAwait(false);

        if (resume is null)
        {
            return Result<ResumeDetail>.Failure(Error.NotFound(
                "resumes.not-found",
                $"Resume '{query.Id}' was not found."));
        }

        // Chunks are fetched with a second, ordered query rather than an Include: the text payload is
        // large, so materialising it in chunk order client-side keeps the mapping trivial and avoids
        // an unordered navigation collection.
        var chunkRows = await _db.ResumeChunks
            .AsNoTracking()
            .Where(c => c.ResumeId == resume.Id)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => new { c.Id, c.ChunkIndex, c.TextContent, c.Embedding })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var chunks = chunkRows
            .Select(c => new ResumeChunkItem(
                c.Id,
                c.ChunkIndex,
                c.TextContent?.Length ?? 0,
                c.Embedding is not null,
                c.TextContent ?? string.Empty))
            .ToList();

        return Result<ResumeDetail>.Success(new ResumeDetail(
            resume.Id,
            resume.CandidateName,
            ResumeFileName.Derive(resume.FilePath),
            resume.FilePath,
            resume.Status,
            resume.FailureReason,
            resume.CreatedAt,
            resume.LastSyncedAt,
            chunks.Count,
            chunks.Count(c => c.HasEmbedding),
            chunks));
    }
}
