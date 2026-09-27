using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Portion.Server.Configuration;
using Portion.Server.Services.Abstractions;

namespace Portion.Server.Services
{
    public class OllamaService : IOllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OllamaService> _logger;
        private readonly OllamaOptions _options;

        public OllamaService(HttpClient httpClient, ILogger<OllamaService> logger, IOptions<OllamaOptions> options)
        {
            _logger = logger;
            _options = options.Value;

            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(_options.BaseUrl);
            _httpClient.Timeout = _options.TimeoutSeconds > 0
                ? TimeSpan.FromSeconds(_options.TimeoutSeconds)
                : Timeout.InfiniteTimeSpan;
        }

        public async Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/api/embeddings", new
                {
                    model = _options.EmbeddingModel,
                    prompt = text
                }, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return null;

                var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: cancellationToken);
                return result?.Embedding;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Embedding generation failed for model {Model}.", _options.EmbeddingModel);
                return null;
            }
        }

        public async IAsyncEnumerable<string> StreamChatAsync(
            string systemPrompt,
            string userPrompt,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var requestBody = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt   }
                },
                stream = true
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
            {
                Content = JsonContent.Create(requestBody)
            };

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                yield return $"[Ollama Error: HTTP {response.StatusCode}. Ensure Ollama is running and '{_options.Model}' is pulled.]";
                yield break;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            bool hasStartedContent = false;

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;

                OllamaChatChunk? chunk = null;
                try { chunk = JsonSerializer.Deserialize<OllamaChatChunk>(line); }
                catch { /* malformed line — skip */ }

                if (chunk?.Message?.Content is { } token)
                {
                    if (!hasStartedContent)
                    {
                        if (string.IsNullOrWhiteSpace(token)) continue;
                        hasStartedContent = true;
                    }
                    yield return token;
                }

                if (chunk?.Done == true) break;
            }
        }

        // ── Private response models ────────────────────────────────────────────

        private sealed class OllamaEmbeddingResponse
        {
            [JsonPropertyName("embedding")]
            public float[]? Embedding { get; set; }
        }

        private sealed class OllamaChatChunk
        {
            [JsonPropertyName("message")] public ChatMessageContent? Message { get; set; }
            [JsonPropertyName("done")]    public bool Done { get; set; }
        }

        private sealed class ChatMessageContent
        {
            [JsonPropertyName("content")] public string? Content { get; set; }
        }
    }
}
