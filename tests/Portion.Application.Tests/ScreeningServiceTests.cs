using Portion.Application.Abstractions;
using Portion.Application.Contracts;

namespace Portion.Application.Tests;

/// <summary>Covers the screening pipeline's ordering guarantees and failure isolation.</summary>
public class ScreeningServiceTests
{
    private static readonly float[] Embedding = [0.1f, 0.2f, 0.3f];

    private static ScoredChunk Chunk(string name, double score) =>
        new(Guid.NewGuid(), name, Guid.NewGuid(), $"{name} content", score);

    [Fact]
    public async Task ExecuteAsync_EmitsTheDocumentedEventOrder()
    {
        var service = new Application.Services.ScreeningService(
            new TestDoubles.StubEmbeddingGenerator(Embedding),
            new StubSearchService(new RetrievalResult([Chunk("Ada", 0.9)], RetrievalMode.Vector)),
            new TestDoubles.StubChatStreamer("hello ", "world"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Application.Services.ScreeningService>.Instance);

        var events = await CollectAsync(service.ExecuteAsync("who is Ada?", 5));

        Assert.Collection(
            events,
            e => AssertStage(e, ScreeningStage.Embedding),
            e => AssertStage(e, ScreeningStage.Search),
            e => Assert.IsType<ScreeningMatchesEvent>(e),
            e => AssertStage(e, ScreeningStage.Generating),
            e => Assert.Equal(new ScreeningTokenEvent("hello "), e),
            e => Assert.Equal(new ScreeningTokenEvent("world"), e),
            e => AssertStage(e, ScreeningStage.Complete),
            e => Assert.IsType<ScreeningDoneEvent>(e));
    }

    [Fact]
    public async Task ExecuteAsync_AnnouncesTheFallbackStage_WhenRetrievalDegraded()
    {
        var service = new Application.Services.ScreeningService(
            new TestDoubles.StubEmbeddingGenerator(Embedding),
            new StubSearchService(new RetrievalResult([new ScoredChunk(Guid.NewGuid(), "Ada", Guid.NewGuid(), "text", 0.4)], RetrievalMode.Keyword)),
            new TestDoubles.StubChatStreamer("answer"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Application.Services.ScreeningService>.Instance);

        var events = await CollectAsync(service.ExecuteAsync("who is Ada?", 5));

        var stages = events.OfType<ScreeningStatusEvent>().Select(e => e.Stage).ToList();

        Assert.Equal(
            [ScreeningStage.Embedding, ScreeningStage.Search, ScreeningStage.Fallback, ScreeningStage.Generating, ScreeningStage.Complete],
            stages);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsTheFallbackStage_WhenVectorSearchSucceeded()
    {
        var service = new Application.Services.ScreeningService(
            new TestDoubles.StubEmbeddingGenerator(Embedding),
            new StubSearchService(new RetrievalResult([new ScoredChunk(Guid.NewGuid(), "Ada", Guid.NewGuid(), "text", 0.9)], RetrievalMode.Vector)),
            new TestDoubles.StubChatStreamer("answer"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Application.Services.ScreeningService>.Instance);

        var events = await CollectAsync(service.ExecuteAsync("who is Ada?", 5));

        Assert.DoesNotContain(events.OfType<ScreeningStatusEvent>(), e => e.Stage == ScreeningStage.Fallback);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesWithoutAnEmbedding_WhenTheEmbeddingModelThrows()
    {
        var service = new Application.Services.ScreeningService(
            new TestDoubles.ThrowingEmbeddingGenerator(),
            new StubSearchService(new RetrievalResult([new ScoredChunk(Guid.NewGuid(), "Ada", Guid.NewGuid(), "text", 0.3)], RetrievalMode.Keyword)),
            new TestDoubles.StubChatStreamer("answer"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Application.Services.ScreeningService>.Instance);

        var events = await CollectAsync(service.ExecuteAsync("who is Ada?", 5));

        // Retrieval still ran and the answer was still produced, which is the whole point of degrading.
        Assert.Contains(events, e => e is ScreeningMatchesEvent);
        Assert.Contains(events, e => e is ScreeningTokenEvent);
        Assert.IsType<ScreeningDoneEvent>(events[^1]);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesAFaultFromTheChatStream_SoTheTransportCanEmitAnErrorEvent()
    {
        var service = new Application.Services.ScreeningService(
            new TestDoubles.StubEmbeddingGenerator(Embedding),
            new StubSearchService(new RetrievalResult([new ScoredChunk(Guid.NewGuid(), "Ada", Guid.NewGuid(), "text", 0.9)], RetrievalMode.Vector)),
            new TestDoubles.FailingChatStreamer(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Application.Services.ScreeningService>.Instance);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in service.ExecuteAsync("who is Ada?", 5))
            {
                // Drain the stream; the fault is expected part-way through.
            }
        });

        Assert.Equal("The chat model went away mid-stream.", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_PassesTheCallersTopKThrough_AndRejectsNonPositiveValues()
    {
        var capture = new StubSearchService(new RetrievalResult([], RetrievalMode.None));

        var service = new Application.Services.ScreeningService(
            new TestDoubles.StubEmbeddingGenerator(Embedding),
            capture,
            new TestDoubles.StubChatStreamer(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Application.Services.ScreeningService>.Instance);

        await CollectAsync(service.ExecuteAsync("question", topK: 17));

        Assert.Equal(17, capture.LastRequest!.TopK);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var _ in service.ExecuteAsync("question", topK: 0))
            {
            }
        });
    }

    private static void AssertStage(ScreeningEvent screeningEvent, ScreeningStage expected) =>
        Assert.Equal(new ScreeningStatusEvent(expected), screeningEvent);

    private static async Task<List<ScreeningEvent>> CollectAsync(IAsyncEnumerable<ScreeningEvent> source)
    {
        var events = new List<ScreeningEvent>();

        await foreach (var screeningEvent in source)
        {
            events.Add(screeningEvent);
        }

        return events;
    }

    /// <summary>A search service that returns a fixed result and records the request it received.</summary>
    private sealed class StubSearchService(RetrievalResult result) : IResumeSearchService
    {
        public ScreeningRequest? LastRequest { get; private set; }

        public Task<RetrievalResult> RetrieveAsync(ScreeningRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            return Task.FromResult(result);
        }
    }
}
