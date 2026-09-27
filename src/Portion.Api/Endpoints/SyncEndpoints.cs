using Microsoft.AspNetCore.Mvc;
using Portion.Api.Configuration;
using Portion.Api.Infrastructure;
using Portion.Application.Contracts;
using Portion.Application.Features.Sync;

namespace Portion.Api.Endpoints;

/// <summary>Maps the repository reconciliation endpoints.</summary>
public static class SyncEndpoints
{
    /// <summary>Route group these endpoints are mounted under.</summary>
    public const string RoutePattern = "/api/v1/recruitment/sync";

    /// <summary>Registers the reconciliation endpoints.</summary>
    public static RouteGroupBuilder MapSyncEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes
            .MapGroup(RoutePattern)
            .WithTags("Sync");

        group.MapPost("", ScheduleAsync)
             .WithName("ScheduleRepositorySync")
             .WithSummary("Schedules a background reconciliation scan of a folder tree.")
             .WithDescription("Returns 202 with a job id. Poll the job resource for discovered, skipped, queued and failed counts.")
             .Produces<RepositorySyncAcceptedResponse>(StatusCodes.Status202Accepted)
             .ProducesProblem(StatusCodes.Status400BadRequest)
             .WithRateLimit(RateLimitPolicies.Mutations);

        group.MapGet("/{jobId:guid}", GetJobAsync)
             .WithName("GetRepositorySyncJob")
             .WithSummary("Returns the current state of a reconciliation job.")
             .Produces<SyncJobResponse>()
             .ProducesProblem(StatusCodes.Status404NotFound)
             .WithRateLimit(RateLimitPolicies.Ingestion);

        return group;
    }

    private static async Task<IResult> ScheduleAsync(
        [FromBody] RepositorySyncRequest? request,
        ScheduleRepositorySyncCommandHandler handler,
        PortionProblemDetailsFactory problems,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new ScheduleRepositorySyncCommand(request?.FolderPath), cancellationToken)
            .ConfigureAwait(false);

        return result.TryGetValue(out var accepted)
            ? Results.Accepted($"{RoutePattern}/{accepted.JobId}", accepted)
            : problems.ToResult(context, result.Error);
    }

    private static async Task<IResult> GetJobAsync(
        Guid jobId,
        GetSyncJobQueryHandler handler,
        PortionProblemDetailsFactory problems,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetSyncJobQuery(jobId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? problems.ToResult(context, result.Error)
            : Results.Ok(result.Value);
    }
}
