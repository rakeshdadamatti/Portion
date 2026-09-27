using Portion.Application.Abstractions;

namespace Portion.Infrastructure.Ingestion;

/// <summary>
/// Drains the reconciliation queue and runs each scan as a tracked background job.
/// </summary>
/// <remarks>
/// The scan deliberately runs under the host's <c>stoppingToken</c> and not the request's aborted
/// token: the client that scheduled the job will have long since received its 202, and cancelling on
/// disconnect would abandon a job the client is still polling.
/// </remarks>
public sealed class RepositorySyncWorker : BackgroundService
{
    /// <summary>How long shutdown waits for the in-flight scan before giving up on a clean drain.</summary>
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);

    private readonly ISyncJobQueue _queue;
    private readonly ISyncJobTracker _tracker;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<RepositorySyncWorker> _logger;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Creates the worker.</summary>
    public RepositorySyncWorker(
        ISyncJobQueue queue,
        ISyncJobTracker tracker,
        IServiceScopeFactory scopeFactory,
        IClock clock,
        ILogger<RepositorySyncWorker> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Repository sync worker started.");

        try
        {
            await foreach (var jobId in _queue.DequeueAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunJobAsync(jobId, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Repository sync worker cancelled.");
        }
        finally
        {
            _drained.TrySetResult();
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Complete();

        using var drainCts = new CancellationTokenSource(DrainTimeout);

        try
        {
            await _drained.Task.WaitAsync(drainCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Repository sync worker did not drain within {TimeoutSeconds}s; continuing shutdown.",
                DrainTimeout.TotalSeconds);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        var snapshot = _tracker.Find(jobId);

        if (snapshot is null)
        {
            _logger.LogWarning("Sync job {JobId} is no longer tracked; dropping it.", jobId);
            return;
        }

        _tracker.TryMarkRunning(jobId, _clock.UtcNow);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<IRepositorySyncService>();

            var counts = await syncService.ReconcileAsync(snapshot.FolderPath, stoppingToken).ConfigureAwait(false);

            _tracker.MarkCompleted(jobId, counts, _clock.UtcNow);

            _logger.LogInformation(
                "Sync job {JobId} completed for {FolderPath}: discovered={Discovered}, skipped={Skipped}, queued={Queued}, failed={Failed}.",
                jobId,
                snapshot.FolderPath,
                counts.Discovered,
                counts.Skipped,
                counts.Queued,
                counts.Failed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _tracker.MarkFailed(jobId, "Application shutdown cancelled the job.", _clock.UtcNow);
            throw;
        }
        catch (Exception ex)
        {
            _tracker.MarkFailed(jobId, ex.Message, _clock.UtcNow);

            _logger.LogError(ex, "Sync job {JobId} failed for {FolderPath}.", jobId, snapshot.FolderPath);
        }
    }
}
