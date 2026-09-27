using System.Diagnostics;

namespace Portion.Api.Infrastructure;

/// <summary>
/// Logs one structured line per request with its method, route, status and duration.
/// </summary>
/// <remarks>
/// Placed outside the exception handler so that a request that ends in a 5xx is still recorded once,
/// with its final status, instead of appearing twice with inconsistent shapes. Message templates use
/// named placeholders so the log stays structured and queryable rather than being pre-interpolated
/// into a string.
/// </remarks>
public sealed class RequestLoggingMiddleware
{
    private const string LogActivityName = "Portion.Api.Request";

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Executes the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();
        string? error = null;

        using var activity = PortionActivitySource.StartServerActivity(LogActivityName, null);

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Re-thrown so the exception handler owns the response; recorded here only to correlate
            // the request line with the fault in the log stream.
            error = ex.GetType().Name;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            if (activity is not null)
            {
                activity.SetTag("http.request.method", context.Request.Method);
                activity.SetTag("url.path", context.Request.Path.Value);
                activity.SetTag("http.response.status_code", context.Response.StatusCode);
            }

            _logger.Log(
                context.Response.StatusCode >= 500 ? LogLevel.Error : LogLevel.Information,
                "Handled {Method} {Path} with {StatusCode} in {ElapsedMilliseconds}ms{ErrorSuffix}",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                error is null ? string.Empty : $" ({error})");
        }
    }
}
