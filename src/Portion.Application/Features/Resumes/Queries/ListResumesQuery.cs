using Microsoft.EntityFrameworkCore;
using Portion.Application.Abstractions;
using Portion.Application.Contracts;
using Portion.Application.Services;
using Portion.Domain.Common;
using Portion.Domain.Entities;

namespace Portion.Application.Features.Resumes.Queries;

/// <summary>Paged, optionally name-filtered resume listing.</summary>
/// <param name="Page">Requested 1-based page, or <c>null</c> for the default.</param>
/// <param name="PageSize">Requested page size, or <c>null</c> for the default.</param>
/// <param name="Search">Case-insensitive candidate-name filter, or <c>null</c>/blank for all.</param>
public sealed record ListResumesQuery(int? Page = null, int? PageSize = null, string? Search = null);

/// <summary>Handles <see cref="ListResumesQuery" />.</summary>
public sealed class ListResumesQueryHandler
{
    private readonly IApplicationDbContext _db;

    /// <summary>Creates the handler.</summary>
    public ListResumesQueryHandler(IApplicationDbContext db) =>
        _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <summary>
    /// Executes the query. Chunk and embedded-chunk counts are computed by projection so that no
    /// denormalised counters have to be added to the frozen resume schema.
    /// </summary>
    public async Task<Result<PagedResult<ResumeListItem>>> HandleAsync(
        ListResumesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Page.TryValidate(query.Page, query.PageSize, out var fieldErrors))
        {
            return Result<PagedResult<ResumeListItem>>.Failure(Error.Validation(
                "resumes.list.invalid-pagination",
                "The paging parameters are outside the supported range.",
                fieldErrors));
        }

        var page = Page.Create(query.Page ?? 1, query.PageSize ?? Page.DefaultPageSize);
        var search = query.Search?.Trim();

        IQueryable<Resume> resumes = _db.Resumes.AsNoTracking();

        if (!string.IsNullOrEmpty(search))
        {
            // EF.Functions.Like keeps the comparison case-insensitive on both providers without
            // relying on a collation that differs between local SQLite and PostgreSQL.
            var pattern = $"%{Escape(search)}%";
            resumes = resumes.Where(r => EF.Functions.Like(r.CandidateName, pattern));
        }

        var totalCount = await resumes.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await resumes
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(page.Skip)
            .Take(page.Size)
            .Select(r => new ResumeListItem(
                r.Id,
                r.CandidateName,
                ResumeFileName.Derive(r.FilePath),
                r.FilePath,
                r.Status,
                r.FailureReason,
                r.CreatedAt,
                r.LastSyncedAt,
                r.Chunks.Count,
                r.Chunks.Count(c => c.Embedding != null)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<ResumeListItem>>.Success(
            PagedResult<ResumeListItem>.Create(rows, page, totalCount));
    }

    private static string Escape(string value) =>
        value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
