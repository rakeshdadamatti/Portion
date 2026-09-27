using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portion.Domain.Common;

namespace Portion.Api.Infrastructure;

/// <summary>
/// Single source of truth for the RFC 7807 payload shape.
/// </summary>
/// <remarks>
/// Every error the API can emit — validation, not-found, conflict, oversized payload, rate limiting,
/// unhandled faults and bare status codes — is produced here, so the wire format is identical no
/// matter which layer raised it. Two extension members are always present: <c>traceId</c>, which
/// correlates a client-visible failure with the server log, and <c>errors</c>, which carries per-field
/// validation messages.
/// </remarks>
public sealed class PortionProblemDetailsFactory
{
    /// <summary>Base URI used to build RFC 7807 <c>type</c> URIs.</summary>
    public const string TypeBaseUri = "https://portion.dev/problems/";

    private readonly IProblemDetailsService _problemDetailsService;

    /// <summary>Creates the factory.</summary>
    public PortionProblemDetailsFactory(IProblemDetailsService problemDetailsService) =>
        _problemDetailsService = problemDetailsService ?? throw new ArgumentNullException(nameof(problemDetailsService));

    /// <summary>Maps a <see cref="ErrorType" /> onto its HTTP status code.</summary>
    public static int ToStatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
        ErrorType.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorType.Failure => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status500InternalServerError
    };

    /// <summary>Builds a ProblemDetails payload for a domain error.</summary>
    public ProblemDetails Create(HttpContext context, Error error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);

        var status = ToStatusCode(error.Type);

        var problem = new ProblemDetails
        {
            Type = TypeBaseUri + Slug(error.Type),
            Title = Title(error.Type),
            Status = status,
            Detail = error.Description,
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        if (error.ValidationErrors is { Count: > 0 })
        {
            problem.Extensions["errors"] = error.ValidationErrors;
        }

        return problem;
    }

    /// <summary>Builds a ProblemDetails payload for an arbitrary status code with no further detail.</summary>
    /// <remarks>
    /// A missing <c>detail</c> falls back to the reason phrase. <c>ProblemDetails.Detail</c> is
    /// annotated <c>JsonIgnoreCondition.WhenWritingNull</c>, so a null detail would silently drop the
    /// member and make the error shape differ depending on which layer raised the fault. Clients can
    /// then rely on <c>detail</c> always being there.
    /// </remarks>
    public ProblemDetails CreateForStatus(HttpContext context, int status, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = new ProblemDetails
        {
            Type = TypeBaseUri + SlugForStatus(status),
            Title = ReasonPhrase(status),
            Status = status,
            Detail = string.IsNullOrWhiteSpace(detail) ? ReasonPhrase(status) : detail,
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        return problem;
    }

    /// <summary>
    /// Wraps a ProblemDetails payload in an <see cref="IResult" />.
    /// </summary>
    /// <remarks>
    /// Endpoints return results rather than writing to the response directly: that keeps every
    /// handler a pure mapping from input to output, lets the routing layer finish writing the body,
    /// and produces <c>application/problem+json</c> from the shared <c>ProblemHttpResult</c> behaviour
    /// instead of a hand-written content type.
    /// </remarks>
    public IResult ToResult(HttpContext context, Error error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);

        return Results.Problem(Create(context, error));
    }

    /// <summary>Wraps a bare-status ProblemDetails payload in an <see cref="IResult" />.</summary>
    public IResult ToResultForStatus(HttpContext context, int status, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Results.Problem(CreateForStatus(context, status, detail));
    }

    /// <summary>Writes a ProblemDetails payload, setting the status and content type.</summary>
    public Task WriteAsync(HttpContext context, ProblemDetails problem)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(problem);

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        return _problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = null
        }).AsTask();
    }

    /// <summary>
    /// Reason phrase for the statuses this API can produce.
    /// </summary>
    /// <remarks>
    /// <c>Microsoft.AspNetCore.WebUtilities.ReasonPhrases</c> would also work, but it is a separate
    /// package and its table is larger than the handful of statuses the API actually emits. Keeping
    /// the map local means the titles are explicit and testable.
    /// </remarks>
    private static string ReasonPhrase(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status405MethodNotAllowed => "Method Not Allowed",
        StatusCodes.Status406NotAcceptable => "Not Acceptable",
        StatusCodes.Status408RequestTimeout => "Request Timeout",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status413PayloadTooLarge => "Payload Too Large",
        StatusCodes.Status415UnsupportedMediaType => "Unsupported Media Type",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        StatusCodes.Status500InternalServerError => "Internal Server Error",
        StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
        StatusCodes.Status504GatewayTimeout => "Gateway Timeout",
        _ => "Request Failed"
    };

    private static string Slug(ErrorType type) => type switch
    {
        ErrorType.Validation => "validation-failed",
        ErrorType.NotFound => "resource-not-found",
        ErrorType.Conflict => "conflict",
        ErrorType.PayloadTooLarge => "payload-too-large",
        ErrorType.RateLimited => "rate-limit-exceeded",
        _ => "internal-error"
    };

    private static string SlugForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "validation-failed",
        StatusCodes.Status404NotFound => "resource-not-found",
        StatusCodes.Status405MethodNotAllowed => "method-not-allowed",
        StatusCodes.Status406NotAcceptable => "not-acceptable",
        StatusCodes.Status413PayloadTooLarge => "payload-too-large",
        StatusCodes.Status415UnsupportedMediaType => "unsupported-media-type",
        StatusCodes.Status429TooManyRequests => "rate-limit-exceeded",
        _ => "request-failed"
    };

    private static string Title(ErrorType type) => type switch
    {
        ErrorType.Validation => "One or more validation errors occurred.",
        ErrorType.NotFound => "The requested resource was not found.",
        ErrorType.Conflict => "The request conflicts with the current state of the resource.",
        ErrorType.PayloadTooLarge => "The payload is too large.",
        ErrorType.RateLimited => "Too many requests.",
        _ => "An unexpected error occurred."
    };
}
