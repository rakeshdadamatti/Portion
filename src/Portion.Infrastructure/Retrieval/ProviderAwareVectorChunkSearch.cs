using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Portion.Application.Abstractions;
using Portion.Application.Services;

namespace Portion.Infrastructure.Retrieval;

/// <summary>
/// Vector nearest-neighbour search with one branch point per provider.
/// </summary>
/// <remarks>
/// <para>
/// On PostgreSQL the ranking is executed by the database: pgvector's <c>CosineDistance</c> translates
/// to the <c>&lt;=&gt;</c> operator, which uses an index and never materialises the corpus in the
/// application.
/// </para>
/// <para>
/// On SQLite there is no vector type, so the same ranking is computed in memory from the JSON
/// <c>float[]</c> column. This is the documented zero-configuration trade-off: correct results, no
/// index-assisted search. Keeping both branches here means the application layer never references
/// pgvector and the retrieval cascade can be unit-tested against a fake.
/// </para>
/// </remarks>
public sealed class ProviderAwareVectorChunkSearch : IVectorChunkSearch
{
    private readonly IApplicationDbContext _db;

    /// <summary>Creates the search strategy.</summary>
    public ProviderAwareVectorChunkSearch(IApplicationDbContext db) =>
        _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScoredChunk>> FindNearestAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);

        if (queryEmbedding.Length == 0)
        {
            return [];
        }

        return _db.ProviderKind == DatabaseProviderKind.Postgres
            ? await SearchInDatabaseAsync(queryEmbedding, topK, cancellationToken).ConfigureAwait(false)
            : await SearchInMemoryAsync(queryEmbedding, topK, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ScoredChunk>> SearchInDatabaseAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        var target = new Vector(queryEmbedding);

        // The distance is selected rather than only ordered, so the score can be derived from the same
        // translated expression without relying on arithmetic inside the projection.
        var rows = await _db.ResumeChunks
            .AsNoTracking()
            .Where(c => c.Embedding != null)
            .OrderBy(c => c.Embedding!.CosineDistance(target))
            .Take(topK)
            .Select(c => new
            {
                c.Id,
                c.ResumeId,
                c.TextContent,
                c.Resume.CandidateName,
                Distance = c.Embedding!.CosineDistance(target)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new ScoredChunk(
                row.ResumeId,
                row.CandidateName,
                row.Id,
                row.TextContent,
                Math.Round(1d - row.Distance, VectorMath.ScorePrecision, MidpointRounding.AwayFromZero)))
            .ToList();
    }

    private async Task<IReadOnlyList<ScoredChunk>> SearchInMemoryAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        // The vector itself is projected (not a derived expression, which the query provider cannot
        // translate) and materialised before the ranking is computed in the application.
        var rows = await _db.ResumeChunks
            .AsNoTracking()
            .Where(c => c.Embedding != null)
            .Select(c => new
            {
                c.Id,
                c.ResumeId,
                c.TextContent,
                c.Resume.CandidateName,
                c.Embedding
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new
            {
                Row = row,
                Score = VectorMath.SimilarityScore(queryEmbedding, row.Embedding!.ToArray())
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Row.Id)
            .Take(topK)
            .Select(x => new ScoredChunk(
                x.Row.ResumeId,
                x.Row.CandidateName,
                x.Row.Id,
                x.Row.TextContent,
                x.Score))
            .ToList();
    }
}
