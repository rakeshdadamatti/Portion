using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Portion.Api.Configuration;
using Portion.Api.Contracts;
using Portion.Api.Infrastructure;
using Portion.Application.Abstractions;
using Portion.Domain.Common;

namespace Portion.Api.Endpoints;

/// <summary>Maps the Server-Sent Events screening endpoint.</summary>
public static class ScreeningEndpoints
{
    /// <summary>Route group this endpoint is mounted under.</summary>
    public const string RoutePattern = "/api/v1/screening";

    /// <summary>Registers the screening endpoint.</summary>
    public static RouteGroupBuilder MapScreeningEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes
            .MapGroup(RoutePattern)
            .WithTags("Screening");

        group.MapGet("/stream", StreamAsync)
             .WithName("StreamScreeningAnswer")
             .WithSummary("Answers a recruiter question as a Server-Sent Events stream.")
             .WithDescription(
                 "Emits a fixed event order: status(embedding), status(search), status(fallback) when retrieval " +
                 "degraded, matches, status(generating), token*, status(complete), done. A downstream fault is " +
                 "reported as a terminal 'error' event rather than a 500, because the response has already " +
                 "committed to 200 by the time generation starts.")
             .DisableAntiforgery()
             .Produces<string>(StatusCodes.Status200OK, SseWriter.ContentType)
             .ProducesProblem(StatusCodes.Status400BadRequest)
             .ProducesProblem(StatusCodes.Status429TooManyRequests)
             .WithRateLimit(RateLimitPolicies.Screening);

        return group;
    }

    private static async Task<IResult> StreamAsync(
        [FromQuery] string? prompt,
        [FromQuery] int? topK,
        IScreeningService screeningService,
        PortionProblemDetailsFactory problems,
        IOptions<ScreeningOptions> options,
        ILoggerFactory loggerFactory,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var question = prompt?.Trim() ?? string.Empty;

        // Validated before the response commits, so a bad request is still a real 400 ProblemDetails
        // response rather than an error frame inside a 200 stream.
        if (question.Length < settings.MinPromptLength || question.Length > settings.MaxPromptLength)
        {
            return problems.ToResult(
                context,
                Error.Validation(
                    "screening.invalid-prompt",
                    "The prompt is missing or outside the supported length range.",
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["prompt"] =
                        [
                            $"Prompt must be between {settings.MinPromptLength} and {settings.MaxPromptLength} characters."
                        ]
                    }));
        }

        if (topK is < 1 || topK > settings.MaxTopK)
        {
            return problems.ToResult(
                context,
                Error.Validation(
                    "screening.invalid-topk",
                    "The topK parameter is outside the supported range.",
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["topK"] = [$"TopK must be between 1 and {settings.MaxTopK}."]
                    }));
        }

        var effectiveTopK = topK ?? settings.DefaultTopK;
        var writer = new SseWriter(context);
        var logger = loggerFactory.CreateLogger("Portion.Api.Screening");

        writer.PrepareResponse();

        try
        {
            await foreach (var screeningEvent in screeningService
                               .ExecuteAsync(question, effectiveTopK, context.RequestAborted)
                               .WithCancellation(context.RequestAborted)
                               .ConfigureAwait(false))
            {
                await writer.WriteAsync(screeningEvent, context.RequestAborted).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client hung up mid-stream. Nothing can be written to a closed connection and an
            // abandoned request is not a fault, so it is recorded at information level only.
            logger.LogInformation("Screening stream for prompt length {PromptLength} was aborted by the client.", question.Length);
        }
        catch (Exception ex)
        {
            // A C# async iterator cannot yield inside a try/catch, so ScreeningService propagates
            // failures. Once 200 has been committed the only correct response is a terminal SSE error
            // frame, so the client-visible message is a fixed string and the detail is logged.
            logger.LogError(ex, "Screening stream failed for prompt length {PromptLength}.", question.Length);

            await writer.WriteEventAsync(
                "error",
                new { message = GlobalExceptionHandler.GenericMessage },
                CancellationToken.None).ConfigureAwait(false);
        }

        return Results.Empty;
    }
}
