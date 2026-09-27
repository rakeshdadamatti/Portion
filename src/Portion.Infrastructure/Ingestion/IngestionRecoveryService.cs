using Microsoft.EntityFrameworkCore;
using Portion.Application.Abstractions;
using Portion.Application.Contracts;
using Portion.Domain.Enums;

namespace Portion.Infrastructure.Ingestion;

/// <summary>
/// Re-queues ingestion work that a previous process left in flight, then releases the start-up gate.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ChannelIngestionQueue" /> is an in-memory bounded channel, so a restart discards
/// everything that was queued but not yet ingested. A resume that was mid-extraction when the
/// process died keeps its <see cref="IngestionStatus.Processing" /> row, and because nothing in the
/// system can advance it, it sits in that state indefinitely. The same applies to a
/// <see cref="IngestionStatus.Discovered" /> row whose process exited between the database insert
/// and the enqueue.
/// </para>
/// <para>
/// This service closes that gap: every unfinished row is either re-queued (when its source file is
/// still on disk) or marked <see cref="IngestionStatus.Failed" /> with an actionable reason (when
/// the file is gone, so re-queueing would only fail again). Recovery therefore converges rather
/// than looping.
/// </para>
/// <para>
/// <see cref="StartGate" /> is released on every exit path. It is awaited by the ingestion worker's
/// start-up, so a slow recovery scan delays the first dequeue instead of racing it, and a recovery
/// failure can never leave the worker blocked forever.
/// </para>
/// <para>
/// Registered after <c>DatabaseInitializer</c> and therefore started after it, so the schema is
/// guaranteed to exist before the scan runs.
/// </para>
/// </remarks>
public sealed class IngestionRecoveryService : IHostedService
{
    /// <summary>Reason recorded when a resume cannot be recovered because its file is missing.</summary>
    public const string MissingSourceReason =
        "Ingestion could not be resumed because the source file is no longer on disk.";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IngestionRecoveryService> _logger;

    /// <summary>Completes once the recovery scan has finished, successfully or not.</summary>
    public TaskCompletionSource StartGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Creates the service.</summary>
    public IngestionRecoveryService(
        IServiceScopeFactory scopeFactory,
        ILogger<IngestionRecoveryService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var queue = scope.ServiceProvider.GetRequiredService<IIngestionQueue>();

            var orphaned = await db.Resumes
                .Where(r => r.Status == IngestionStatus.Processing || r.Status == IngestionStatus.Discovered)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (orphaned.Count == 0)
            {
                _logger.LogInformation("Ingestion recovery found no unfinished resumes.");
                return;
            }

            var requeued = 0;
            var abandoned = 0;

            foreach (var resume in orphaned)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(resume.FilePath))
                {
                    // Re-queueing would fail identically on every subsequent start-up, so the row is
                    // closed out instead of being retried forever.
                    resume.Status = IngestionStatus.Failed;
                    resume.FailureReason = MissingSourceReason;
                    abandoned++;
                    continue;
                }

                resume.Status = IngestionStatus.Processing;
                resume.FailureReason = null;

                await queue
                    .EnqueueAsync(
                        new IngestionRequest(resume.Id, resume.FilePath, IngestionTrigger.Recovery),
                        cancellationToken)
                    .ConfigureAwait(false);

                requeued++;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogWarning(
                "Ingestion recovery found {Total} unfinished resume(s) from a previous process: {Requeued} re-queued, {Abandoned} marked failed because the source file is missing.",
                orphaned.Count,
                requeued,
                abandoned);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Ingestion recovery cancelled during host start-up.");
            throw;
        }
        catch (Exception ex)
        {
            // Recovery is best-effort. Failing the host start-up would take down an application that
            // is otherwise fully functional, so the fault is logged and the workers continue.
            _logger.LogError(ex, "Ingestion recovery failed; unfinished resumes were not re-queued.");
        }
        finally
        {
            StartGate.TrySetResult();
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
