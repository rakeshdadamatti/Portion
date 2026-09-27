using Portion.Application.Abstractions;
using Portion.Application.Contracts;
using Portion.Domain.Common;

namespace Portion.Application.Features.Sync;

/// <summary>Schedules a tracked background reconciliation job for a folder tree.</summary>
/// <param name="FolderPath">Absolute or relative path to scan recursively.</param>
public sealed record ScheduleRepositorySyncCommand(string? FolderPath);

/// <summary>Handles <see cref="ScheduleRepositorySyncCommand" />.</summary>
/// <remarks>
/// The job is registered with <see cref="ISyncJobTracker" /> and handed to a dedicated background
/// queue. The request thread only enqueues; the scan itself runs on the sync worker bound to the
/// application's shutdown token, never to <c>HttpContext.RequestAborted</c>.
/// </remarks>
public sealed class ScheduleRepositorySyncCommandHandler
{
    private readonly ISyncJobTracker _tracker;
    private readonly ISyncJobQueue _queue;
    private readonly ILogger<ScheduleRepositorySyncCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public ScheduleRepositorySyncCommandHandler(
        ISyncJobTracker tracker,
        ISyncJobQueue queue,
        ILogger<ScheduleRepositorySyncCommandHandler> logger)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Executes the command, returning the job identifier the client should poll.</summary>
    public async Task<Result<RepositorySyncAcceptedResponse>> HandleAsync(
        ScheduleRepositorySyncCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var folderPath = command.FolderPath?.Trim();

        if (string.IsNullOrEmpty(folderPath))
        {
            return Result<RepositorySyncAcceptedResponse>.Failure(Error.Validation(
                "sync.folder-path-required",
                "A folder path is required.",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["folderPath"] = ["FolderPath is required and must not be blank."]
                }));
        }

        var jobId = Guid.NewGuid();
        _tracker.Enqueue(jobId, folderPath);

        await _queue.EnqueueAsync(jobId, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Scheduled reconciliation job {JobId} for {FolderPath}.", jobId, folderPath);

        return Result<RepositorySyncAcceptedResponse>.Success(new RepositorySyncAcceptedResponse(
            "Repository reconciliation scheduled.",
            jobId));
    }
}
