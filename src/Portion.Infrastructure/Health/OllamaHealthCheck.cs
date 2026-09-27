using System.Net.Http.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Portion.Infrastructure.Ai;
using Portion.Infrastructure.Configuration;

namespace Portion.Infrastructure.Health;

/// <summary>
/// Readiness probe for the local Ollama server. Hits <c>{BaseUrl}/api/tags</c>, the cheapest endpoint
/// that proves the daemon is up and serving, under a short timeout so a hung daemon cannot delay the
/// health response.
/// </summary>
/// <remarks>
/// An unavailable daemon is reported as <see cref="HealthStatus.Degraded" />, never
/// <see cref="HealthStatus.Unhealthy" />. Screening degrades to keyword retrieval without it, so
/// failing readiness would pull a perfectly serviceable API out of the load balancer's rotation for
/// the sake of a local convenience dependency. The degradation stays visible in the report body.
/// </remarks>
public sealed class OllamaHealthCheck : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OllamaOptions _options;

    /// <summary>Creates the check.</summary>
    public OllamaHealthCheck(IHttpClientFactory httpClientFactory, IOptions<OllamaOptions> options)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProbeTimeout);

        try
        {
            var client = _httpClientFactory.CreateClient(OllamaHttpClient.HttpClientName);

            using var response = await client
                .GetAsync("api/tags", HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Degraded(
                    $"Ollama at {_options.BaseUrl} returned HTTP {(int)response.StatusCode}.");
            }

            var modelCount = await CountModelsAsync(response, timeoutCts.Token).ConfigureAwait(false);

            return HealthCheckResult.Healthy(
                $"Ollama at {_options.BaseUrl} is serving {modelCount} model(s).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded(
                $"Ollama at {_options.BaseUrl} did not respond within {ProbeTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Ollama at {_options.BaseUrl} is unreachable.", ex);
        }
    }

    private static async Task<int> CountModelsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var tags = await response.Content
                .ReadFromJsonAsync<TagList>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return tags?.Models?.Count ?? 0;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            // The daemon is healthy; only the optional model listing could not be parsed.
            return 0;
        }
    }

    private sealed class TagList
    {
        public List<Tag>? Models { get; set; }
    }

    private sealed class Tag
    {
        public string? Name { get; set; }
    }
}
