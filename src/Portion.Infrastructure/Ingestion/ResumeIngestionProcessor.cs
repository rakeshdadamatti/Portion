using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portion.Application.Abstractions;
using Portion.Domain.Entities;
using Portion.Domain.Enums;
using Portion.Infrastructure.Configuration;
using Pgvector;

namespace Portion.Infrastructure.Ingestion;

/// <summary>
/// Runs the extract → chunk → embed → persist pipeline for a single resume.
/// </summary>
/// <remarks>
/// <para>
/// Re-index safety: any chunks already stored for the resume are removed before the fresh set is
/// inserted, so re-processing a resume can never append a second copy of its content.
/// </para>
/// <para>
/// Failure isolation: a fault marks the resume <see cref="IngestionStatus.Failed" /> with a captured
/// reason and returns normally, so a single bad document can never stop the worker from draining the
/// rest of the queue.
/// </para>
/// </remarks>
public sealed class ResumeIngestionProcessor : IResumeIngestionProcessor
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentTextExtractorFactory _extractorFactory;
    private readonly ITextChunker _chunker;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IOptions<IngestionOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<ResumeIngestionProcessor> _logger;

    /// <summary>Creates the processor.</summary>
    public ResumeIngestionProcessor(
        IApplicationDbContext db,
        IDocumentTextExtractorFactory extractorFactory,
        ITextChunker chunker,
        IEmbeddingGenerator embeddingGenerator,
        IOptions<IngestionOptions> options,
        IClock clock,
        ILogger<ResumeIngestionProcessor> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _extractorFactory = extractorFactory ?? throw new ArgumentNullException(nameof(extractorFactory));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task ProcessAsync(IngestionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;

        try
        {
            var resume = await _db.Resumes
                .FirstOrDefaultAsync(r => r.Id == request.ResumeId, cancellationToken)
                .ConfigureAwait(false);

            if (resume is null)
            {
                _logger.LogWarning(
                    "Skipping ingestion for resume {ResumeId}: the row no longer exists.",
                    request.ResumeId);

                return;
            }

            resume.Status = IngestionStatus.Processing;
            resume.FailureReason = null;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var extractor = _extractorFactory.Create(request.FilePath);

            var text = await extractor
                .ExtractAsync(request.FilePath, cancellationToken)
                .ConfigureAwait(false);

            var segments = _chunker.Chunk(text, options.ChunkSize, options.ChunkOverlap);

            _logger.LogInformation(
                "Extracted {CharacterCount} character(s) from {FileName} as {ChunkCount} chunk(s).",
                text.Length,
                Path.GetFileName(request.FilePath),
                segments.Count);

            // Re-index safety: drop the previous chunk set before writing the new one.
            var existingChunkCount = await _db.ResumeChunks
                .Where(c => c.ResumeId == resume.Id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (existingChunkCount > 0)
            {
                _logger.LogInformation(
                    "Replaced {ExistingChunkCount} existing chunk(s) for resume {ResumeId}.",
                    existingChunkCount,
                    resume.Id);
            }

            for (var index = 0; index < segments.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var segment = segments[index];
                var embedding = await _embeddingGenerator
                    .GenerateAsync(segment, cancellationToken)
                    .ConfigureAwait(false);

                _db.ResumeChunks.Add(new ResumeChunk
                {
                    Id = Guid.NewGuid(),
                    ResumeId = resume.Id,
                    TextContent = segment,
                    Embedding = embedding is null ? null : new Vector(embedding),
                    ChunkIndex = index
                });
            }

            resume.Status = IngestionStatus.Synced;
            resume.FailureReason = null;
            resume.LastSyncedAt = _clock.UtcNow.UtcDateTime;

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Resume {ResumeId} ({CandidateName}) synced with {ChunkCount} chunk(s).",
                resume.Id,
                resume.CandidateName,
                segments.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown: mark the row failed and let the worker unwind.
            await MarkFailedAsync(request.ResumeId, "Ingestion cancelled during application shutdown.")
                             .ConfigureAwait(false);

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Ingestion failed for resume {ResumeId} ({FilePath}).",
                request.ResumeId,
                request.FilePath);

            await MarkFailedAsync(request.ResumeId, ex.Message).ConfigureAwait(false);
        }
    }

    private async Task MarkFailedAsync(Guid resumeId, string reason)
    {
        try
        {
            // The pipeline may have already staged chunk inserts, and the row itself may be tracked as
            // Modified. Every tracked entity is detached first: re-querying with a dirty tracker would
            // return the same instance, so assigning the failure state would flush the half-built
            // chunk set along with it.
            _db.ChangeTracker.Clear();

            // Anything already committed for this resume is removed as well, so a failed re-index
            // cannot leave the previous attempt's content behind as a partial index.
            await _db.ResumeChunks
                .Where(c => c.ResumeId == resumeId)
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            var resume = await _db.Resumes
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == resumeId)
                .ConfigureAwait(false);

            if (resume is null)
            {
                return;
            }

            _db.Resumes.Attach(resume);
            resume.Status = IngestionStatus.Failed;
            resume.FailureReason = Truncate(reason, 2000);

            await _db.SaveChangesAsync().ConfigureAwait(false);

            _db.ChangeTracker.Clear();
        }
        catch (Exception markFailure)
        {
            _logger.LogError(
                markFailure,
                "Could not record the failure state for resume {ResumeId}.",
                resumeId);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
