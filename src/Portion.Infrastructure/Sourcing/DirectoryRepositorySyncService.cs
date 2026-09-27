using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portion.Application.Abstractions;
using Portion.Domain.Entities;
using Portion.Domain.Enums;
using Portion.Infrastructure.Configuration;

namespace Portion.Infrastructure.Sourcing;

/// <summary>
/// Reconciles a local folder tree against the resume index.
/// </summary>
/// <remarks>
/// <para>
/// Idempotency comes from content addressing: a file is identified by the SHA-256 of its bytes, not
/// by its path. Re-scanning a folder therefore discovers the same set of documents regardless of how
/// many times it runs, and a file that was merely renamed is recognised rather than re-ingested.
/// </para>
/// <para>
/// A fresh DI scope per file keeps a long scan from accumulating tracked entities, and means a single
/// poisoned document cannot leave the context in a failed state for the remainder of the walk.
/// </para>
/// </remarks>
public sealed class DirectoryRepositorySyncService : IRepositorySyncService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIngestionQueue _queue;
    private readonly IHashCalculator _hasher;
    private readonly IClock _clock;
    private readonly IngestionOptions _options;
    private readonly ILogger<DirectoryRepositorySyncService> _logger;

    /// <summary>Creates the service.</summary>
    public DirectoryRepositorySyncService(
        IServiceScopeFactory scopeFactory,
        IIngestionQueue queue,
        IHashCalculator hasher,
        IClock clock,
        IOptions<IngestionOptions> options,
        ILogger<DirectoryRepositorySyncService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SyncJobCounts> ReconcileAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"The folder '{folderPath}' does not exist or is not accessible.");
        }

        var supported = new HashSet<string>(_options.SupportedExtensions, StringComparer.OrdinalIgnoreCase);

        var candidates = Directory
            .EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(path => supported.Contains(Path.GetExtension(path)))
            .ToList();

        _logger.LogInformation(
            "Reconciliation scan of {FolderPath} found {FileCount} supported document(s).",
            folderPath,
            candidates.Count);

        var discovered = 0;
        var skipped = 0;
        var queued = 0;
        var failed = 0;

        foreach (var path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            discovered++;

            try
            {
                if (await ProcessCandidateAsync(path, cancellationToken).ConfigureAwait(false))
                {
                    queued++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One unreadable file must not abandon the rest of the walk.
                failed++;
                _logger.LogWarning(ex, "Skipping {FilePath} during reconciliation.", path);
            }
        }

        return new SyncJobCounts(discovered, skipped, queued, failed);
    }

    /// <summary>Returns true when the document was queued for ingestion, false when it was already synced.</summary>
    private async Task<bool> ProcessCandidateAsync(string path, CancellationToken cancellationToken)
    {
        var hash = await _hasher.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var existing = await db.Resumes
            .FirstOrDefaultAsync(r => r.FileHash == hash, cancellationToken)
            .ConfigureAwait(false);

        if (existing is { Status: IngestionStatus.Synced })
        {
            return false;
        }

        var resume = existing ?? new Resume
        {
            Id = Guid.NewGuid(),
            CandidateName = Path.GetFileNameWithoutExtension(path),
            FilePath = path,
            FileHash = hash,
            Status = IngestionStatus.Discovered,
            CreatedAt = _clock.UtcNow.UtcDateTime,
            LastSyncedAt = _clock.UtcNow.UtcDateTime
        };

        resume.CandidateName = Path.GetFileNameWithoutExtension(path);
        resume.FilePath = path;
        resume.FileHash = hash;
        resume.Status = IngestionStatus.Processing;
        resume.FailureReason = null;

        if (existing is null)
        {
            db.Resumes.Add(resume);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _queue
            .EnqueueAsync(new IngestionRequest(resume.Id, path, IngestionTrigger.RepositorySync), cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Queued {CandidateName} ({FilePath}) for ingestion as resume {ResumeId}.",
            resume.CandidateName,
            path,
            resume.Id);

        return true;
    }
}
