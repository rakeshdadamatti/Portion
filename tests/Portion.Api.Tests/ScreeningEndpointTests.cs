using System.Net;
using System.Text;
using System.Text.Json;
using Portion.Application.Contracts;

namespace Portion.Api.Tests;

/// <summary>Covers the Server-Sent Events contract of the screening endpoint.</summary>
public class ScreeningEndpointTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task Stream_EmitsTheContractedEventOrder()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().GetAsync(
            "/api/v1/screening/stream?prompt=Who%20worked%20on%20engines&topK=3",
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var frames = await ReadFramesAsync(response);

        Assert.Equal(
            ["status", "status", "status", "matches", "status", "token", "token", "status", "done"],
            frames.Select(frame => frame.Event));

        Assert.Equal("embedding", Stage(frames[0]));
        Assert.Equal("search", Stage(frames[1]));
        Assert.Equal("fallback", Stage(frames[2]));
        Assert.Equal("generating", Stage(frames[4]));
        Assert.Equal("complete", Stage(frames[7]));

        // 3 comes from the query string, not from Screening:DefaultTopK.
        Assert.Equal(3, factory.Screening.LastTopK);
        Assert.Equal("Who worked on engines", factory.Screening.LastQuery);
    }

    [Fact]
    public async Task Stream_CarriesTheMatchesPayload()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().GetAsync(
            "/api/v1/screening/stream?prompt=engineers",
            HttpCompletionOption.ResponseHeadersRead);

        var matches = (await ReadFramesAsync(response)).Single(frame => frame.Event == "matches");

        using var document = JsonDocument.Parse(matches.Data);

        Assert.Equal(1, document.RootElement.GetProperty("topChunkCount").GetInt32());

        var candidate = document.RootElement.GetProperty("candidates").EnumerateArray().Single();

        Assert.Equal("Ada", candidate.GetProperty("candidateName").GetString());
        Assert.Equal(1, candidate.GetProperty("chunkCount").GetInt32());
    }

    [Fact]
    public async Task Stream_EscapesNewlinesInsideAToken()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().GetAsync(
            "/api/v1/screening/stream?prompt=engineers",
            HttpCompletionOption.ResponseHeadersRead);

        var tokens = (await ReadFramesAsync(response))
            .Where(frame => frame.Event == "token")
            .Select(frame => frame.Data)
            .ToList();

        Assert.Equal(2, tokens.Count);

        // The second token contains a real newline. If it were written raw the frame would split in
        // two and the client would desynchronise, so it must arrive JSON-escaped on one data line and
        // decode back to the original.
        Assert.DoesNotContain('\n', tokens[1]);

        Assert.Equal("worked on the engine.\n", JsonSerializer.Deserialize<string>(tokens[1]));
    }

    [Fact]
    public async Task Stream_CarriesTheDoneTiming()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().GetAsync(
            "/api/v1/screening/stream?prompt=engineers",
            HttpCompletionOption.ResponseHeadersRead);

        var done = (await ReadFramesAsync(response)).Single(frame => frame.Event == "done");

        using var document = JsonDocument.Parse(done.Data);

        Assert.Equal(42, document.RootElement.GetProperty("elapsedMs").GetInt64());
    }

    [Fact]
    public async Task Stream_RejectsABlankPrompt_WithAValidationProblem()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().GetAsync("/api/v1/screening/stream?prompt=%20%20");

        var problem = await ResumeEndpointTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation-failed");

        Assert.True(problem.GetProperty("errors").TryGetProperty("prompt", out _), "The offending field must be named.");
    }

    [Fact]
    public async Task Stream_RejectsAnOutOfRangeTopK()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().GetAsync("/api/v1/screening/stream?prompt=engineers&topK=5000");

        var problem = await ResumeEndpointTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation-failed");

        Assert.True(problem.GetProperty("errors").TryGetProperty("topK", out _), "The offending field must be named.");
    }

    [Fact]
    public async Task Stream_ReportsADownstreamFaultAsATerminalErrorFrame()
    {
        await factory.ResetDatabaseAsync();
        factory.Screening.Fault = new InvalidOperationException("ollama exploded: secret detail");

        try
        {
            using var response = await factory.CreateClient().GetAsync(
                "/api/v1/screening/stream?prompt=engineers",
                HttpCompletionOption.ResponseHeadersRead);

            // 200, not 500: the response had already committed to an event stream by then.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var frames = await ReadFramesAsync(response);

            var error = frames.Last();
            Assert.Equal("error", error.Event);

            using var document = JsonDocument.Parse(error.Data);

            // The exception message must not reach the client, only a fixed string.
            Assert.DoesNotContain("secret detail", error.Data, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("message").GetString()));
        }
        finally
        {
            factory.Screening.Fault = null;
        }
    }

    private static string Stage(SseFrame frame)
    {
        using var document = JsonDocument.Parse(frame.Data);

        return document.RootElement.GetProperty("stage").GetString()!;
    }

    private static async Task<IReadOnlyList<SseFrame>> ReadFramesAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var frames = new List<SseFrame>();
        var current = new StringBuilder();
        string? eventName = null;

        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length == 0)
            {
                if (eventName is not null)
                {
                    frames.Add(new SseFrame(eventName, current.ToString()));
                }

                eventName = null;
                current.Clear();
                continue;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventName = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                current.Append(line["data: ".Length..]);
            }
        }

        return frames;
    }

    private sealed record SseFrame(string Event, string Data);
}
