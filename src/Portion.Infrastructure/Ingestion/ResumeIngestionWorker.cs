using Portion.Application.Abstractions;
using Portion.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Portion.Infrastructure.Ingestion;

/// <summary>
/// The single owner of resume ingestion execution: drains <see cref="IIngestionQueue" /> and
/// dispatches each item to a freshly scoped <see cref="IResumeIngestionProcessor" />.
/// </summary>
/// <remarks>
/// <para>
/// A DI scope per item is what makes the scoped <see cref="IApplicationDbContext" /> safe: a DbContext
/// is never shared across two resumptions of this loop, so a long-running worker cannot accumulate
/// tracked entities or cross-contaminate change tracking between documents.
/// </para>
/// <para>
/// Shutdown completes the queue and then waits, bounded, for the in-flight item to finish so a
/// half-written resume is not left in <c>Processing</c> state. The processor converts a mid-flight
/// cancellation into a <c>Failed</c> row rather than losing the document.
/// </para>
/// </remarks>
public sealed class ResumeIngestionWorker : BackgroundService
{
    /// <summary>How long shutdown waits for the in-flight document before giving up on a clean drain.</summary>
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);

    private readonly IIngestionQueue _queue;
    private readonly IngestionRecoveryService? _recovery;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResumeIngestionWorker> _logger;
    private readonly IOptions<IngestionOptions> _options;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Creates the worker.</summary>
    public ResumeIngestionWorker(
        IIngestionQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<IngestionOptions> options,
        ILogger<ResumeIngestionWorker> logger,
        IngestionRecoveryService? recovery = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _recovery = recovery;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Resume ingestion worker started (chunkSize: {ChunkSize} words, overlap: {ChunkOverlap} words).",
            _options.Value.ChunkSize,
            _options.Value.ChunkOverlap);

        try
        {
            // The recovery scan re-queues work a previous process left unfinished. Draining before it
            // finishes would interleave recovered and newly-uploaded items unpredictably, so the first
            // dequeue waits. The gate is released on every recovery exit path, including failure, so
            // this can never block start-up indefinitely.
            if (_recovery is not null)
            {
                await _recovery.StartGate.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
            }

            await foreach (var request in _queue.DequeueAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await ProcessSafelyAsync(request, stoppingToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Resume ingestion worker drained the queue.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Resume ingestion worker cancelled.");
        }
        finally
        {
            _drained.TrySetResult();
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Complete the queue first so the reader finishes naturally, then bound the wait so a wedged
        // extraction cannot hold up application shutdown indefinitely.
        _queue.Complete();

        using var drainCts = new CancellationTokenSource(DrainTimeout);

        try
        {
            await _drained.Task.WaitAsync(drainCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Resume ingestion worker did not drain within {TimeoutSeconds}s; continuing shutdown.",
                DrainTimeout.TotalSeconds);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessSafelyAsync(IngestionRequest request, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IResumeIngestionProcessor>();

        try
        {
            await processor.ProcessAsync(request, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The processor already records a Failed row; this guard exists so that an unexpected
            // failure in scope resolution or dispatch cannot tear down the worker.
            _logger.LogError(
                ex,
                "Unhandled error while dispatching ingestion for resume {ResumeId}; the worker continues.",
                request.ResumeId);
        }
    }
}
