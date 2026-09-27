using Microsoft.Extensions.Options;
using Portion.Application.Abstractions;
using Portion.Infrastructure.Configuration;

namespace Portion.Infrastructure.Ai;

/// <summary>
/// Produces embeddings through the configured Ollama embedding model.
/// </summary>
/// <remarks>
/// Returning <c>null</c> — rather than throwing — is a deliberate part of the
/// <see cref="IEmbeddingGenerator" /> contract: ingestion must still index the text of a resume when
/// the local model is unavailable, and retrieval must degrade to keyword search. A genuine transport
/// fault is logged so the degradation is visible.
/// </remarks>
public sealed class OllamaEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly OllamaHttpClient _client;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaEmbeddingGenerator> _logger;

    /// <summary>Creates the generator.</summary>
    public OllamaEmbeddingGenerator(
        OllamaHttpClient client,
        IOptions<OllamaOptions> options,
        ILogger<OllamaEmbeddingGenerator> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<float[]?> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return await _client
                .RequestEmbeddingAsync(_options.EmbeddingModel, text, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Embedding generation failed for model {EmbeddingModel}; the chunk will be stored without a vector.",
                _options.EmbeddingModel);

            return null;
        }
    }
}
