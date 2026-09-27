using Microsoft.Extensions.Options;
using Portion.Application.Abstractions;
using Portion.Infrastructure.Configuration;

namespace Portion.Infrastructure.Ai;

/// <summary>Streams chat completions from the configured Ollama chat model.</summary>
public sealed class OllamaChatCompletionStreamer : IChatCompletionStreamer
{
    private readonly OllamaHttpClient _client;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaChatCompletionStreamer> _logger;

    /// <summary>Creates the streamer.</summary>
    public OllamaChatCompletionStreamer(
        OllamaHttpClient client,
        IOptions<OllamaOptions> options,
        ILogger<OllamaChatCompletionStreamer> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(systemPrompt);
        ArgumentNullException.ThrowIfNull(userPrompt);

        _logger.LogDebug("Streaming chat completion from model {Model}.", _options.Model);

        return _client.StreamChatAsync(_options.Model, systemPrompt, userPrompt, cancellationToken);
    }
}
