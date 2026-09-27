using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Portion.Api.Configuration;
using Portion.Api.Infrastructure;

namespace Portion.Api.Endpoints;

/// <summary>Maps the liveness and readiness probes.</summary>
public static class HealthEndpoints
{
    /// <summary>Tag selecting the checks that prove the process is alive.</summary>
    public const string LiveTag = "live";

    /// <summary>Tag selecting the checks that prove the API can serve traffic.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Maps <c>/health</c>, <c>/health/live</c> and <c>/health/ready</c>.</summary>
    /// <remarks>
    /// <c>MapHealthChecks</c> returns an <see cref="IEndpointConventionBuilder" /> rather than a
    /// <c>RouteHandlerBuilder</c>, and the API explorer does not surface these routes, so they are
    /// added to the OpenAPI document by <c>HealthProbeDocumentFilter</c> instead of by metadata here.
    /// </remarks>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // The aggregate endpoint is the readiness view: it includes everything tagged 'ready'.
        routes.MapHealthChecks("/health", ReadyOptions())
              .WithTags("Health")
              .WithName("Health")
              .WithSummary("Aggregate health report. Identical to the readiness probe.")
              .AllowAnonymous()
              .WithRateLimit(RateLimitPolicies.Health);

        routes.MapHealthChecks("/health/live", LiveOptions())
               .WithTags("Health")
               .AllowAnonymous()
               .WithName("HealthLive")
               .WithSummary("Liveness probe. Fails only if the process itself cannot serve requests.")
               .WithRateLimit(RateLimitPolicies.Health);

        routes.MapHealthChecks("/health/ready", ReadyOptions())
               .WithTags("Health")
               .AllowAnonymous()
               .WithName("HealthReady")
               .WithSummary("Readiness probe. Includes the database check and reports Ollama degradation.")
               .WithRateLimit(RateLimitPolicies.Health);

        return routes;
    }

    private static HealthCheckOptions LiveOptions() => new()
    {
        Predicate = registration => registration.Tags.Contains(LiveTag),
        ResponseWriter = WriteJsonAsync,
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status200OK,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
        }
    };

    private static HealthCheckOptions ReadyOptions() => new()
    {
        Predicate = registration => registration.Tags.Contains(ReadyTag),
        ResponseWriter = WriteJsonAsync,
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            // Ollama being down degrades retrieval quality but the API still serves traffic, so
            // readiness stays 200 and the degradation is visible in the body.
            [HealthStatus.Degraded] = StatusCodes.Status200OK,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
        }
    };

    /// <summary>
    /// Writes a minimal JSON health document: the aggregate status plus each check's description and
    /// elapsed time. Deliberately terse — the payload is polled frequently and must not leak
    /// connection strings or exception details.
    /// </summary>
    private static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2)
            })
        };

        return context.Response.WriteAsJsonAsync(payload);
    }
}
