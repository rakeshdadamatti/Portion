using Portion.Application.Contracts;

namespace Portion.Application.Abstractions;

/// <summary>How a resume was scheduled for ingestion.</summary>
public enum IngestionTrigger
{
    /// <summary>Uploaded through the API.</summary>
    Upload = 0,

    /// <summary>Discovered by a folder reconciliation scan.</summary>
    RepositorySync = 1,

    /// <summary>Recovered from an interrupted state left by a previous process.</summary>
    Recovery = 2
}

/// <summary>A unit of ingestion work handed to the background worker.</summary>
public sealed record IngestionRequest(
    Guid ResumeId,
    string FilePath,
    IngestionTrigger Trigger = IngestionTrigger.Upload);

/// <summary>A bounded, single-reader work queue for ingestion.</summary>
public interface IIngestionQueue
{
    /// <summary>Number of items currently waiting to be processed.</summary>
    int Count { get; }

    /// <summary>Enqueues work, awaiting capacity when the bounded channel is full.</summary>
    ValueTask EnqueueAsync(IngestionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Streams queued work until the channel completes or the token is signalled.</summary>
    IAsyncEnumerable<IngestionRequest> DequeueAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Signals that no further work will be enqueued.</summary>
    void Complete();
}

/// <summary>
/// Executes the extract, chunk, embed and persist pipeline for a single resume. Invoked only by the
/// ingestion background worker, which owns queue draining and per-item scoping.
/// </summary>
public interface IResumeIngestionProcessor
{
    /// <summary>Processes one resume. Implementations must be idempotent.</summary>
    Task ProcessAsync(IngestionRequest request, CancellationToken cancellationToken = default);
}
