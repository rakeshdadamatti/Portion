using System.Diagnostics;

namespace Portion.Api.Infrastructure;

/// <summary>
/// The API's diagnostic source. Named so traces are attributable to this service in any APM backend.
/// </summary>
public static class PortionActivitySource
{
    /// <summary>Name of the application activity source.</summary>
    public const string SourceName = "Portion.Api";

    /// <summary>Name of the HTTP server activity source, mirroring the conventional ASP.NET Core name.</summary>
    public const string HttpSourceName = "Portion.Api.Http";

    private static readonly ActivitySource Application = new(SourceName, "1.0.0");
    private static readonly ActivitySource Http = new(HttpSourceName, "1.0.0");

    /// <summary>Starts an application-level activity, e.g. one screening exchange.</summary>
    public static Activity? StartActivity(string operationName, ActivityKind kind = ActivityKind.Internal) =>
        Application.StartActivity(operationName, kind);

    /// <summary>
    /// Starts a server activity parented to an inbound trace identifier, so work started during a
    /// request joins the caller's distributed trace instead of beginning a new one.
    /// </summary>
    public static Activity? StartServerActivity(string operationName, string? parentTraceId)
    {
        if (string.IsNullOrWhiteSpace(parentTraceId) || parentTraceId.Length != TraceIdHexLength)
        {
            return Http.StartActivity(operationName, ActivityKind.Server);
        }

        try
        {
            return Http.StartActivity(
                operationName,
                ActivityKind.Server,
                parentContext: new ActivityContext(
                    ActivityTraceId.CreateFromString(parentTraceId),
                    ActivitySpanId.CreateRandom(),
                    ActivityTraceFlags.Recorded));
        }
        catch (FormatException)
        {
            // A malformed W3C traceparent is the caller's problem, not a reason to fail the request:
            // start an unparented activity and let the work proceed.
            return Http.StartActivity(operationName, ActivityKind.Server);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Http.StartActivity(operationName, ActivityKind.Server);
        }
    }

    /// <summary>Length of a hex-encoded W3C trace identifier.</summary>
    private const int TraceIdHexLength = 32;
}
