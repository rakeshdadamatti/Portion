using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Portion.Application.Abstractions;
using Portion.Domain.Entities;

namespace Portion.Application.Services;

/// <summary>
/// Executes the retrieval cascade that grounds a screening answer in indexed resume content.
/// </summary>
/// <remarks>
/// Order is fixed and intentional: vector similarity first, then a SQL <c>LIKE</c> keyword sweep,
/// then the first available chunks. The cascade never throws on an empty corpus — that case is
/// reported as <see cref="RetrievalMode.None" /> so the caller can substitute the "no resumes
/// indexed" notice. Keyword and first-available results carry a decayed positional score rather
/// than a true similarity, because no embedding comparison took place for them.
/// </remarks>
public sealed class ResumeSearchService : IResumeSearchService
{
    /// <summary>Highest score assigned to a non-vector retrieval result.</summary>
    private const double NonVectorCeiling = 0.5d;

    private readonly IApplicationDbContext _db;
    private readonly IVectorChunkSearch _vectorSearch;

    /// <summary>Creates the service.</summary>
    public ResumeSearchService(IApplicationDbContext db, IVectorChunkSearch vectorSearch)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _vectorSearch = vectorSearch ?? throw new ArgumentNullException(nameof(vectorSearch));
    }

    /// <inheritdoc />
    public async Task<RetrievalResult> RetrieveAsync(
        ScreeningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.TopK, 1);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new RetrievalResult([], RetrievalMode.None);
        }

        // 1. Vector similarity. The provider-specific translation lives behind IVectorChunkSearch so
        //    that pgvector's CosineDistance never has to be referenced from this layer.
        if (request.QueryEmbedding is { Length: > 0 } embedding)
        {
            var nearest = await _vectorSearch
                .FindNearestAsync(embedding, request.TopK, cancellationToken)
                .ConfigureAwait(false);

            if (nearest.Count > 0)
            {
                return new RetrievalResult(nearest, RetrievalMode.Vector);
            }
        }

        // 2. Keyword fallback via SQL LIKE.
        var keywordMatches = await LoadChunksAsync(
                c => EF.Functions.Like(c.TextContent, BuildKeywordPattern(request.Query)),
                request.TopK,
                cancellationToken)
            .ConfigureAwait(false);

        if (keywordMatches.Count > 0)
        {
            return new RetrievalResult(keywordMatches, RetrievalMode.Keyword);
        }

        // 3. First available chunks, so the model still has something grounded to work with.
        //    `c => true` rather than a null predicate: expression trees cannot hold a null constant
        //    for a non-nullable value type, and EF elides the tautological WHERE.
        var firstChunks = await LoadChunksAsync(_ => true, request.TopK, cancellationToken).ConfigureAwait(false);

        return firstChunks.Count > 0
            ? new RetrievalResult(firstChunks, RetrievalMode.FirstAvailable)
            : new RetrievalResult([], RetrievalMode.None);
    }

    private async Task<IReadOnlyList<ScoredChunk>> LoadChunksAsync(
        Expression<Func<ResumeChunk, bool>> predicate,
        int topK,
        CancellationToken cancellationToken)
    {
        IQueryable<ResumeChunk> query = _db.ResumeChunks
            .AsNoTracking()
            .Where(predicate)
            .OrderByDescending(c => c.Resume.CreatedAt)
            .ThenBy(c => c.ResumeId)
            .ThenBy(c => c.ChunkIndex);

        // Projected to an anonymous type so the LINQ translation stays provider-agnostic and no
        // non-public type ever reaches the compiled query provider.
        var rows = await query
            .Take(topK)
            .Select(c => new { c.Id, c.ResumeId, c.TextContent, c.Resume.CandidateName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select((row, index) => new ScoredChunk(
                row.ResumeId,
                row.CandidateName,
                row.Id,
                row.TextContent,
                DecayedScore(index, rows.Count)))
            .ToList();
    }

    /// <summary>
    /// Escapes LIKE metacharacters so a literal '%' or '_' in the recruiter's question cannot widen
    /// the pattern. The backslash escape is understood by both SQLite and PostgreSQL.
    /// </summary>
    private static string BuildKeywordPattern(string query)
    {
        var escaped = query
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }

    private static double DecayedScore(int index, int total) =>
        Math.Round(
            NonVectorCeiling * (1d - index / (double)Math.Max(total, 1)),
            VectorMath.ScorePrecision,
            MidpointRounding.AwayFromZero);
}
