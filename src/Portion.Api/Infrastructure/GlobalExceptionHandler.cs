using Microsoft.AspNetCore.Diagnostics;

namespace Portion.Api.Infrastructure;

/// <summary>
/// Last-resort handler. Renders every unrecognised fault as a 500 ProblemDetails payload whose
/// <c>detail</c> is a fixed string.
/// </summary>
/// <remarks>
/// The exception is logged in full but never surfaced to the client: a stack trace, a SQL fragment or
/// a file path in a response body is a disclosure bug, so the only client-visible correlation handle is
/// the <c>traceId</c>. The transport-level faults that <c>BadHttpRequestException</c> represents (an
/// oversized body, a malformed request line) are re-mapped to their intended status, because those
/// are the caller's problem and returning 500 for them would be misleading.
/// </remarks>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    /// <summary>Message returned for every unexpected fault.</summary>
    public const string GenericMessage = "An unexpected error occurred while processing the request.";

    private const string ClientAbortMessage = "The client closed the connection before the response was complete.";

    private readonly PortionProblemDetailsFactory _problems;
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly bool _includeExceptionDetails;

    /// <summary>Creates the handler.</summary>
    public GlobalExceptionHandler(
        PortionProblemDetailsFactory problems,
        ILogger<GlobalExceptionHandler> logger,
        bool includeExceptionDetails = false)
    {
        _problems = problems ?? throw new ArgumentNullException(nameof(problems));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _includeExceptionDetails = includeExceptionDetails;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // Nothing useful can be written to a connection the caller already abandoned.
            _logger.LogInformation(
                "Request {Method} {Path} was aborted by the client.",
                httpContext.Request.Method,
                httpContext.Request.Path);

            return true;
        }

        if (exception is BadHttpRequestException badRequest)
        {
            var status = badRequest.StatusCode is >= 400 and < 500 ? badRequest.StatusCode : StatusCodes.Status400BadRequest;

            _logger.LogInformation(
                "Rejected a malformed request {Method} {Path} with status {Status}.",
                httpContext.Request.Method,
                httpContext.Request.Path,
                status);

            var transportProblem = _problems.CreateForStatus(httpContext, status);
            await _problems.WriteAsync(httpContext, transportProblem).ConfigureAwait(false);

            return true;
        }

        _logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path}.",
            httpContext.Request.Method,
            httpContext.Request.Path);

        var problem = _problems.CreateForStatus(
            httpContext,
            StatusCodes.Status500InternalServerError,
            _includeExceptionDetails ? exception.Message : GenericMessage);

        await _problems.WriteAsync(httpContext, problem).ConfigureAwait(false);

        return true;
    }

    /// <summary>Client-facing text used when a request is abandoned mid-response.</summary>
    public static string ClientAbortDetail => ClientAbortMessage;
}
