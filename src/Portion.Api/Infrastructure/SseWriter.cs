using System.Text.Json;
using Portion.Application.Contracts;

namespace Portion.Api.Infrastructure;

/// <summary>
/// Writes the Server-Sent Events wire format for screening responses.
/// </summary>
/// <remarks>
/// <para>
/// Every payload is JSON-encoded and written on a single line. The <c>token</c> event in particular
/// must never contain a raw newline: a model delta can easily contain one, and an unescaped newline
/// would terminate the SSE frame and desynchronise the client. Round-tripping the token through
/// <c>JsonSerializer</c> makes that structurally impossible — the client un-escapes the JSON string
/// rather than guessing at a delimiter convention.
/// </para>
/// <para>
/// Named events (<c>event:</c> + <c>data:</c>) replace the previous <c>[STATUS]</c> string-prefix
/// convention, so the browser can dispatch on a real event type instead of parsing prose.
/// </para>
/// </remarks>
public sealed class SseWriter
{
    /// <summary>Content type of an SSE response.</summary>
    public const string ContentType = "text/event-stream";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpContext _context;

    /// <summary>Creates a writer bound to a response.</summary>
    public SseWriter(HttpContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// Sets the SSE response headers. Called before the first byte is written, so the client can
    /// commit to an event stream immediately rather than buffering while waiting for the first token.
    /// </summary>
    public void PrepareResponse()
    {
        var response = _context.Response;

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = ContentType;
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        // Disables proxy buffering (nginx and several CDNs buffer SSE by default, which defeats
        // streaming entirely).
        response.Headers["X-Accel-Buffering"] = "no";
    }

    /// <summary>Serialises a typed screening event into the correct named SSE frame.</summary>
    public Task WriteAsync(ScreeningEvent screeningEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(screeningEvent);

        return screeningEvent switch
        {
            ScreeningStatusEvent status => WriteEventAsync("status", new { stage = StageName(status.Stage) }, cancellationToken),
            ScreeningMatchesEvent matches => WriteEventAsync("matches", new
            {
                topChunkCount = matches.TopChunkCount,
                candidates = matches.Candidates
            }, cancellationToken),
            ScreeningTokenEvent token => WriteEventAsync("token", token.Token, cancellationToken),
            ScreeningDoneEvent done => WriteEventAsync("done", new { elapsedMs = done.ElapsedMs }, cancellationToken),
            ScreeningErrorEvent error => WriteEventAsync("error", new { message = error.Message }, cancellationToken),
            _ => Task.CompletedTask
        };
    }

    /// <summary>Writes a raw named event with a JSON-serialisable payload.</summary>
    public async Task WriteEventAsync<T>(string eventName, T payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        cancellationToken.ThrowIfCancellationRequested();

        var json = JsonSerializer.Serialize(payload, SerializerOptions);

        // Serialising the whole frame as one interpolation guarantees the `data:` line stays single
        // line even when the payload contains newlines (System.Text.Json escapes them as \n).
        var frame = $"event: {eventName}\ndata: {json}\n\n";

        await _context.Response.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await _context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string StageName(ScreeningStage stage) => stage switch
    {
        ScreeningStage.Embedding => "embedding",
        ScreeningStage.Search => "search",
        ScreeningStage.Fallback => "fallback",
        ScreeningStage.Generating => "generating",
        ScreeningStage.Complete => "complete",
        _ => "search"
    };
}
