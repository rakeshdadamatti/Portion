using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portion.Infrastructure.Ai;

/// <summary>
/// Typed <see cref="HttpClient" /> for the local Ollama server. Owns the base address and timeout so
/// that no caller has to reconfigure the client, and is created through
/// <c>IHttpClientFactory</c> so sockets are pooled and rotated correctly.
/// </summary>
public sealed class OllamaHttpClient
{
    /// <summary>Name of the underlying named <see cref="HttpClient" />, shared with the health check.</summary>
    public const string HttpClientName = "ollama";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    /// <summary>Creates the client.</summary>
    public OllamaHttpClient(HttpClient httpClient) =>
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    /// <summary>Requests an embedding for a single prompt.</summary>
    public async Task<float[]?> RequestEmbeddingAsync(
        string model,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsJsonAsync("/api/embeddings", new { model, prompt }, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content
            .ReadFromJsonAsync<EmbeddingResponse>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return payload?.Embedding is { Length: > 0 } embedding ? embedding : null;
    }

    /// <summary>
    /// Streams chat deltas, throwing on any non-success status.
    /// </summary>
    /// <remarks>
    /// A failure is signalled by exception rather than by a diagnostic token so that the caller
    /// cannot mistake a transport error for model output: a diagnostic string would be delivered to
    /// the browser as a <c>token</c> event and rendered as part of the answer. The screening stream
    /// converts the exception into a terminal SSE <c>error</c> event.
    /// </remarks>
    public async IAsyncEnumerable<string> StreamChatAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = true
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(body, options: SerializerOptions)
        };

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // A 503 is Ollama's way of reporting that the requested model is not pulled, which is the
            // single most common misconfiguration; it is called out explicitly to shorten diagnosis.
            var hint = response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable
                ? " The configured chat model may not be pulled."
                : string.Empty;

            throw new OllamaRequestException(
                $"Ollama returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) for the chat request.{hint}");
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using var reader = new StreamReader(stream);

        // ReadLineAsync returns null at end of stream, which avoids the synchronous EndOfStream probe
        // that CA2024 flags in an async method.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ChatChunk? chunk = null;

            try
            {
                chunk = JsonSerializer.Deserialize<ChatChunk>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                // Ollama interleaves keep-alive and non-JSON lines; a malformed one is not fatal.
                continue;
            }

            if (chunk?.Message?.Content is { Length: > 0 } content)
            {
                yield return content;
            }

            if (chunk?.Done == true)
            {
                yield break;
            }
        }
    }

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }

    private sealed class ChatChunk
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}

/// <summary>Raised when Ollama rejects a request or answers with a non-success status.</summary>
public sealed class OllamaRequestException : Exception
{
    /// <summary>Creates the exception.</summary>
    public OllamaRequestException(string message) : base(message) { }
}
