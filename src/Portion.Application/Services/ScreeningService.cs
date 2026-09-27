using System.Diagnostics;
using System.Runtime.CompilerServices;
using Portion.Application.Abstractions;
using Portion.Application.Contracts;

namespace Portion.Application.Services;

/// <summary>
/// Drives a recruiter's screening question end to end and emits a strictly ordered event stream.
/// </summary>
/// <remarks>
/// Emission order is part of the public contract consumed by the browser:
/// <c>status:embedding</c> → <c>status:search</c> → (<c>status:fallback</c> when the cascade had to
/// degrade) → <c>matches</c> → <c>status:generating</c> → <c>token</c>* → <c>status:complete</c> →
/// <c>done</c>. A recoverable embedding outage degrades to keyword retrieval; any other downstream
/// fault propagates so the transport can emit a terminal <c>error</c> event and close the stream
/// cleanly. (A C# async iterator cannot <c>yield</c> inside a <c>try</c> block that has a
/// <c>catch</c>, so error rendering is deliberately left to the caller.)
/// </remarks>
public sealed class ScreeningService : IScreeningService
{
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IResumeSearchService _searchService;
    private readonly IChatCompletionStreamer _streamer;
    private readonly ILogger<ScreeningService> _logger;

    /// <summary>Creates the service.</summary>
    public ScreeningService(
        IEmbeddingGenerator embeddingGenerator,
        IResumeSearchService searchService,
        IChatCompletionStreamer streamer,
        ILogger<ScreeningService> logger)
    {
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _streamer = streamer ?? throw new ArgumentNullException(nameof(streamer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ScreeningEvent> ExecuteAsync(
        string query,
        int topK,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);

        var stopwatch = Stopwatch.StartNew();

        yield return new ScreeningStatusEvent(ScreeningStage.Embedding);

        float[]? embedding = null;
        try
        {
            embedding = await _embeddingGenerator.GenerateAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A missing embedding is expected when Ollama is offline; retrieval degrades to keyword.
            // No yield appears in this catch, which keeps the async iterator legal.
            _logger.LogWarning(ex, "Query embedding failed; falling back to keyword retrieval.");
            embedding = null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        yield return new ScreeningStatusEvent(ScreeningStage.Search);

        var retrieval = await _searchService
            .RetrieveAsync(new ScreeningRequest(query, topK, embedding), cancellationToken)
            .ConfigureAwait(false);

        if (retrieval.Mode is RetrievalMode.Keyword or RetrievalMode.FirstAvailable)
        {
            yield return new ScreeningStatusEvent(ScreeningStage.Fallback);
        }

        yield return new ScreeningMatchesEvent(retrieval.Chunks.Count, retrieval.ToCandidates());

        cancellationToken.ThrowIfCancellationRequested();
        yield return new ScreeningStatusEvent(ScreeningStage.Generating);

        var groundedContext = retrieval.ToGroundedContext();
        var userPrompt = PromptBuilder.BuildUserPrompt(groundedContext, query);

        var tokenCount = 0;

        await foreach (var token in _streamer
                           .StreamAsync(PromptBuilder.SystemPrompt, userPrompt, cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            tokenCount++;
            yield return new ScreeningTokenEvent(token);
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Screening stream completed: topK={TopK}, mode={Mode}, chunks={Chunks}, tokens={Tokens}, elapsedMs={ElapsedMs}",
            topK,
            retrieval.Mode,
            retrieval.Chunks.Count,
            tokenCount,
            stopwatch.ElapsedMilliseconds);

        yield return new ScreeningStatusEvent(ScreeningStage.Complete);
        yield return new ScreeningDoneEvent(stopwatch.ElapsedMilliseconds);
    }
}
