using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Portion.Api.Infrastructure;

/// <summary>Per-endpoint rate limiter declarations.</summary>
public static class RateLimitEndpointExtensions
{
    /// <summary>Applies a named limiter to a single endpoint.</summary>
    /// <remarks>
    /// The limiter is only <em>named</em> here: the actual <c>RateLimiterOptions</c> are configured once
    /// in the composition root. That keeps the policy definitions and the endpoint annotations in
    /// separate places, so adding an endpoint cannot change a global budget by accident.
    /// </remarks>
    public static TBuilder WithRateLimit<TBuilder>(this TBuilder builder, string policyName)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);

        return builder.WithMetadata(new EnableRateLimitingAttribute(policyName));
    }
}

/// <summary>Registration of the fixed-window limiter policies.</summary>
public static class RateLimitRegistration
{
    /// <summary>Permits per endpoint, per client partition.</summary>
    public static int ScreeningPermitLimit { get; set; } = 20;

    /// <summary>Window applied to the screening limiter.</summary>
    public static TimeSpan ScreeningWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Permits per window for uploads and listing.</summary>
    public static int IngestionPermitLimit { get; set; } = 60;

    /// <summary>Window applied to the ingestion limiter.</summary>
    public static TimeSpan IngestionWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Permits per window for reconciliation scheduling and deletes.</summary>
    public static int MutationsPermitLimit { get; set; } = 30;

    /// <summary>Window applied to the mutation limiter.</summary>
    public static TimeSpan MutationsWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Permits per window for health probes. Generous, because a load balancer polls hard.</summary>
    public static int HealthPermitLimit { get; set; } = 240;

    /// <summary>Window applied to the health limiter.</summary>
    public static TimeSpan HealthWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Adds every named policy to the global rate limiter.</summary>
    public static IServiceCollection AddPortionRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddRateLimiter(options =>
        {
            // A rejected request is answered with RFC 7807, using the same factory as every other
            // failure, rather than the framework's plain-text 429 body.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var problems = context.HttpContext.RequestServices.GetRequiredService<PortionProblemDetailsFactory>();

                await problems.WriteAsync(
                    context.HttpContext,
                    problems.Create(
                        context.HttpContext,
                        Domain.Common.Error.RateLimited(
                            "http.rate-limit-exceeded",
                            "Too many requests. Retry after the current window elapses.")))
                    .ConfigureAwait(false);
            };

            options.AddPolicy(Configuration.RateLimitPolicies.Screening, context =>
                CreateFixedWindow(context, ScreeningPermitLimit, ScreeningWindow));

            options.AddPolicy(Configuration.RateLimitPolicies.Ingestion, context =>
                CreateFixedWindow(context, IngestionPermitLimit, IngestionWindow));

            options.AddPolicy(Configuration.RateLimitPolicies.Mutations, context =>
                CreateFixedWindow(context, MutationsPermitLimit, MutationsWindow));

            options.AddPolicy(Configuration.RateLimitPolicies.Health, context =>
                CreateFixedWindow(context, HealthPermitLimit, HealthWindow));
        });
    }

    /// <summary>
    /// Builds a fixed-window limiter partitioned by client IP.
    /// </summary>
    /// <remarks>
    /// A fixed window is chosen over a sliding one because the memory cost is a single counter per
    /// partition, which keeps this safe to run in-process. The queue limit of zero means requests over
    /// the budget are rejected immediately instead of queuing, which is what an API caller wants: a
    /// queued request that eventually fails has already consumed the connection and the wait time.
    /// </remarks>
    private static RateLimitPartition<string> CreateFixedWindow(HttpContext context, int permitLimit, TimeSpan window)
    {
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    }
}
