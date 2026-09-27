using Portion.Application.Abstractions;
using Portion.Application.Contracts;
using Portion.Domain.Common;

namespace Portion.Application.Features.Sync;

/// <summary>Fetches the current state of a reconciliation job.</summary>
/// <param name="JobId">Job identity returned by the schedule endpoint.</param>
public sealed record GetSyncJobQuery(Guid JobId);

/// <summary>Handles <see cref="GetSyncJobQuery" />.</summary>
public sealed class GetSyncJobQueryHandler
{
    private readonly ISyncJobTracker _tracker;

    /// <summary>Creates the handler.</summary>
    public GetSyncJobQueryHandler(ISyncJobTracker tracker) =>
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));

    /// <summary>Executes the query, returning a not-found error for an unknown or evicted job.</summary>
    public Task<Result<SyncJobResponse>> HandleAsync(GetSyncJobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = _tracker.Find(query.JobId);

        if (snapshot is null)
        {
            return Task.FromResult(Result<SyncJobResponse>.Failure(Error.NotFound(
                "sync.job-not-found",
                $"Sync job '{query.JobId}' was not found.")));
        }

        return Task.FromResult(Result<SyncJobResponse>.Success(new SyncJobResponse(
            snapshot.JobId,
            snapshot.FolderPath,
            snapshot.State,
            snapshot.Discovered,
            snapshot.Skipped,
            snapshot.Queued,
            snapshot.Failed,
            snapshot.StartedAt,
            snapshot.CompletedAt,
            snapshot.Error)));
    }
}
