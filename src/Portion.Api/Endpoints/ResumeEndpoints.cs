using Microsoft.AspNetCore.Mvc;
using Portion.Api.Configuration;
using Portion.Api.Contracts;
using Portion.Api.Infrastructure;
using Portion.Application.Contracts;
using Portion.Application.Features.Resumes.Commands;
using Portion.Application.Features.Resumes.Queries;
using Portion.Domain.Common;

namespace Portion.Api.Endpoints;

/// <summary>Maps the <c>/api/v1/recruitment/resumes</c> resource.</summary>
public static class ResumeEndpoints
{
    /// <summary>Route group these endpoints are mounted under.</summary>
    public const string RoutePattern = "/api/v1/recruitment/resumes";

    /// <summary>Registers the resume endpoints.</summary>
    public static RouteGroupBuilder MapResumeEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes
            .MapGroup(RoutePattern)
            .WithTags("Resumes");

        group.MapGet("", ListAsync)
             .WithName("ListResumes")
             .WithSummary("Lists ingested resumes, newest first.")
             .WithDescription("Supports a case-insensitive candidate-name filter via the 'q' parameter.")
             .Produces<PagedResponse<ResumeListItem>>()
             .ProducesProblem(StatusCodes.Status400BadRequest)
             .ProducesProblem(StatusCodes.Status429TooManyRequests)
             .WithRateLimit(RateLimitPolicies.Ingestion);

        group.MapGet("/{id:guid}", GetAsync)
             .WithName("GetResume")
             .WithSummary("Returns one resume together with its extracted chunks.")
             .Produces<ResumeDetail>()
             .ProducesProblem(StatusCodes.Status404NotFound)
             .WithRateLimit(RateLimitPolicies.Ingestion);

        // Minimal APIs cannot infer multipart binding, so the form part is declared explicitly.
        // Antiforgery is not enabled because this API is stateless and authenticated by a bearer token
        // at the edge; a cookie-based antiforgery token would add no protection here.
        group.MapPost("", UploadAsync)
             .WithName("UploadResume")
             .WithSummary("Accepts a resume upload and queues it for background ingestion.")
             .WithDescription("Returns 202 with the resume id. Poll the detail endpoint for chunk and embedding counts.")
             .DisableAntiforgery()
             .Accepts<IFormFile>("multipart/form-data")
             .Produces<UploadAcceptedResponse>(StatusCodes.Status202Accepted)
             .ProducesProblem(StatusCodes.Status400BadRequest)
             .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
             .ProducesProblem(StatusCodes.Status429TooManyRequests)
             .WithRateLimit(RateLimitPolicies.Ingestion);

        group.MapDelete("/{id:guid}", DeleteAsync)
             .WithName("DeleteResume")
             .WithSummary("Deletes a resume and, optionally, its managed source file.")
             .WithDescription("The file is only removed when it lives inside the managed upload root, so documents indexed from an external folder are never deleted.")
             .Produces(StatusCodes.Status204NoContent)
             .ProducesProblem(StatusCodes.Status404NotFound)
             .WithRateLimit(RateLimitPolicies.Mutations);

        return group;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? q,
        ListResumesQueryHandler handler,
        PortionProblemDetailsFactory problems,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new ListResumesQuery(page, pageSize, q), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? problems.ToResult(context, result.Error)
            : Results.Ok(result.Value.ToHttpResponse());
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetResumeQueryHandler handler,
        PortionProblemDetailsFactory problems,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetResumeQuery(id), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? problems.ToResult(context, result.Error)
            : Results.Ok(result.Value);
    }

    private static async Task<IResult> UploadAsync(
        // Deliberately unannotated. IFormFile infers form binding by convention, and Swashbuckle
        // cannot generate a parameter for an explicit [FromForm] IFormFile — it throws and turns
        // /swagger/v1/swagger.json into a 500. The multipart request body is described by
        // ResumeUploadOpenApiFilter instead.
        IFormFile? file,
        UploadResumeCommandHandler handler,
        PortionProblemDetailsFactory problems,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return problems.ToResult(
                context,
                Error.Validation(
                    "resumes.upload.missing-file",
                    "A multipart 'file' part is required.",
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["file"] = ["Attach the resume as multipart/form-data using the field name 'file'."]
                    }));
        }

        // Owned here so the upload stream is disposed even when the handler rejects the file.
        await using var content = file.OpenReadStream();

        var result = await handler
            .HandleAsync(new UploadResumeCommand(file.FileName, content, file.Length), cancellationToken)
            .ConfigureAwait(false);

        return result.TryGetValue(out var accepted)
            ? Results.Accepted($"{RoutePattern}/{accepted.ResumeId}", accepted)
            : problems.ToResult(context, result.Error);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        [FromQuery] bool? deleteFile,
        DeleteResumeCommandHandler handler,
        PortionProblemDetailsFactory problems,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new DeleteResumeCommand(id, deleteFile ?? false), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? problems.ToResult(context, result.Error)
            : Results.NoContent();
    }
}
