namespace Portion.Application.Abstractions;

/// <summary>Lifecycle of a repository reconciliation job.</summary>
public enum SyncJobState
{
    /// <summary>Accepted and waiting for the sync worker.</summary>
    Queued = 0,

    /// <summary>Scanning the folder tree.</summary>
    Running = 1,

    /// <summary>Finished; consult the counters.</summary>
    Completed = 2,

    /// <summary>Aborted with an error; see <see cref="SyncJobSnapshot.Error" />.</summary>
    Failed = 3
}

/// <summary>Immutable view of a reconciliation job.</summary>
public sealed record SyncJobSnapshot(
    Guid JobId,
    string FolderPath,
    SyncJobState State,
    int Discovered,
    int Skipped,
    int Queued,
    int Failed,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error)
{
    /// <summary>Creates a freshly queued job snapshot.</summary>
    public static SyncJobSnapshot CreateQueued(Guid jobId, string folderPath) =>
        new(jobId, folderPath, SyncJobState.Queued, 0, 0, 0, 0, null, null, null);
}

/// <summary>Counts produced by one reconciliation pass.</summary>
public sealed record SyncJobCounts(int Discovered, int Skipped, int Queued, int Failed)
{
    /// <summary>All-zero counts.</summary>
    public static SyncJobCounts None { get; } = new(0, 0, 0, 0);
}

/// <summary>Tracks reconciliation job state for polling by clients.</summary>
public interface ISyncJobTracker
{
    /// <summary>Registers a new queued job and returns its snapshot.</summary>
    SyncJobSnapshot Enqueue(Guid jobId, string folderPath);

    /// <summary>Transitions a job to <see cref="SyncJobState.Running" />.</summary>
    bool TryMarkRunning(Guid jobId, DateTimeOffset startedAt);

    /// <summary>Merges the latest counters into a running job.</summary>
    void UpdateCounts(Guid jobId, SyncJobCounts counts);

    /// <summary>Transitions a job to <see cref="SyncJobState.Completed" /> with final counters.</summary>
    void MarkCompleted(Guid jobId, SyncJobCounts counts, DateTimeOffset completedAt);

    /// <summary>Transitions a job to <see cref="SyncJobState.Failed" />.</summary>
    void MarkFailed(Guid jobId, string error, DateTimeOffset completedAt);

    /// <summary>Returns the snapshot, or <c>null</c> when the job is unknown or has been evicted.</summary>
    SyncJobSnapshot? Find(Guid jobId);
}

/// <summary>Reconciles a local folder tree against the resume index.</summary>
public interface IRepositorySyncService
{
    /// <summary>
    /// Scans <paramref name="folderPath" /> recursively, deduplicates by content hash, and enqueues
    /// newly discovered or previously failed documents for ingestion. Must honour
    /// <paramref name="cancellationToken" /> — this is bound to application shutdown, never to a
    /// request, because the job outlives the HTTP call that scheduled it.
    /// </summary>
    Task<SyncJobCounts> ReconcileAsync(
        string folderPath,
        CancellationToken cancellationToken = default);
}

/// <summary>Tracked background-job queue for reconciliation requests.</summary>
public interface ISyncJobQueue
{
    /// <summary>Enqueues a job identifier for the reconciliation worker.</summary>
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Streams queued job identifiers until completion or cancellation.</summary>
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Signals that no further jobs will be enqueued.</summary>
    void Complete();
}
