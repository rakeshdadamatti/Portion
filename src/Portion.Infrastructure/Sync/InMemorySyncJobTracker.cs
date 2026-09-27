using System.Collections.Concurrent;
using Portion.Application.Abstractions;

namespace Portion.Infrastructure.Sync;

/// <summary>
/// Process-local reconciliation job registry backed by a bounded <see cref="ConcurrentDictionary" />.
/// </summary>
/// <remarks>
/// <para>
/// Bounded rather than unbounded so that a client repeatedly scheduling scans cannot grow the
/// dictionary without limit. When the bound is reached the oldest <em>terminal</em> job is evicted
/// first; a running job is only evicted when nothing else is available, so a job that is still being
/// polled is never silently lost mid-scan.
/// </para>
/// <para>
/// Counters are updated on a per-field basis so a client polling during a long scan sees progress
/// rather than a single terminal snapshot.
/// </para>
/// </remarks>
public sealed class InMemorySyncJobTracker : ISyncJobTracker
{
    private const int DefaultCapacity = 500;

    private readonly ConcurrentDictionary<Guid, SyncJobSnapshot> _jobs = new();
    private readonly ILogger<InMemorySyncJobTracker> _logger;
    private readonly int _capacity;
    private readonly Lock _evictionLock = new();

    /// <summary>Creates the tracker.</summary>
    public InMemorySyncJobTracker(
        ILogger<InMemorySyncJobTracker> logger,
        int capacity = DefaultCapacity)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>Number of jobs currently retained.</summary>
    public int Count => _jobs.Count;

    /// <inheritdoc />
    public SyncJobSnapshot Enqueue(Guid jobId, string folderPath)
    {
        var snapshot = SyncJobSnapshot.CreateQueued(jobId, folderPath);
        _jobs[jobId] = snapshot;

        EvictIfNeeded();

        return snapshot;
    }

    /// <inheritdoc />
    public bool TryMarkRunning(Guid jobId, DateTimeOffset startedAt) =>
        Mutate(jobId, current => current with
        {
            State = SyncJobState.Running,
            StartedAt = current.StartedAt ?? startedAt
        }) is not null;

    /// <inheritdoc />
    public void UpdateCounts(Guid jobId, SyncJobCounts counts) =>
        Mutate(jobId, current => current with
        {
            Discovered = counts.Discovered,
            Skipped = counts.Skipped,
            Queued = counts.Queued,
            Failed = counts.Failed
        });

    /// <inheritdoc />
    public void MarkCompleted(Guid jobId, SyncJobCounts counts, DateTimeOffset completedAt) =>
        Mutate(jobId, current => current with
        {
            State = SyncJobState.Completed,
            Discovered = counts.Discovered,
            Skipped = counts.Skipped,
            Queued = counts.Queued,
            Failed = counts.Failed,
            CompletedAt = completedAt,
            Error = null
        });

    /// <inheritdoc />
    public void MarkFailed(Guid jobId, string error, DateTimeOffset completedAt) =>
        Mutate(jobId, current => current with
        {
            State = SyncJobState.Failed,
            CompletedAt = completedAt,
            Error = string.IsNullOrWhiteSpace(error) ? "The job failed for an unknown reason." : error
        });

    /// <inheritdoc />
    public SyncJobSnapshot? Find(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var snapshot) ? snapshot : null;

    private SyncJobSnapshot? Mutate(Guid jobId, Func<SyncJobSnapshot, SyncJobSnapshot> update)
    {
        if (_jobs.TryGetValue(jobId, out var current))
        {
            return _jobs.AddOrUpdate(jobId, current, (_, existing) => update(existing));
        }

        return null;
    }

    private void EvictIfNeeded()
    {
        if (_jobs.Count <= _capacity)
        {
            return;
        }

        lock (_evictionLock)
        {
            while (_jobs.Count > _capacity)
            {
                // Prefer retiring a finished job; only touch an in-flight job as a last resort.
                var candidate = _jobs.Values
                    .Where(j => j.State is SyncJobState.Completed or SyncJobState.Failed)
                    .OrderBy(j => j.CompletedAt ?? DateTimeOffset.MinValue)
                    .Select(j => (Guid?)j.JobId)
                    .FirstOrDefault()
                    ?? _jobs.Keys.Select(id => (Guid?)id).FirstOrDefault();

                if (candidate is not { } jobIdToRemove || !_jobs.TryRemove(jobIdToRemove, out var removed))
                {
                    break;
                }

                _logger.LogDebug("Evicted sync job {JobId} to stay within the {Capacity} job bound.", removed, _capacity);
            }
        }
    }
}
