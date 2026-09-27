using Portion.Application.Abstractions;
using Portion.Application.Features.Resumes.Commands;

namespace Portion.Application.Tests;

/// <summary>
/// Test doubles shared by the application-layer tests.
/// </summary>
/// <remarks>
/// Hand-written rather than generated: these interfaces are small and stable, and an explicit fake
/// documents the exact behaviour a test depends on, which a mocking framework's output does not.
/// </remarks>
internal static class TestDoubles
{
    /// <summary>An embedding generator that returns a fixed vector, or null to simulate no model.</summary>
    internal sealed class StubEmbeddingGenerator(float[]? result) : IEmbeddingGenerator
    {
        public int CallCount { get; private set; }

        public Task<float[]?> GenerateAsync(string text, CancellationToken cancellationToken = default)
        {
            CallCount++;

            return Task.FromResult(result);
        }
    }

    /// <summary>An embedding generator that always throws, standing in for an offline model.</summary>
    internal sealed class ThrowingEmbeddingGenerator : IEmbeddingGenerator
    {
        public Task<float[]?> GenerateAsync(string text, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("The embedding model is unavailable.");
    }

    /// <summary>A vector search that returns a pre-arranged ranking.</summary>
    internal sealed class StubVectorSearch : IVectorChunkSearch
    {
        private readonly ScoredChunk[] _chunks;

        public StubVectorSearch(params ScoredChunk[] chunks) => _chunks = chunks;

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ScoredChunk>> FindNearestAsync(
            float[] queryEmbedding,
            int topK,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            return Task.FromResult<IReadOnlyList<ScoredChunk>>(_chunks.Take(topK).ToList());
        }
    }

    /// <summary>A vector search that reports an empty result, forcing the cascade to degrade.</summary>
    internal sealed class EmptyVectorSearch : IVectorChunkSearch
    {
        public Task<IReadOnlyList<ScoredChunk>> FindNearestAsync(
            float[] queryEmbedding,
            int topK,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScoredChunk>>([]);
    }

    /// <summary>A chat streamer that replays a fixed token sequence.</summary>
    internal sealed class StubChatStreamer(params string[] tokens) : IChatCompletionStreamer
    {
        public async IAsyncEnumerable<string> StreamAsync(
            string systemPrompt,
            string userPrompt,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            foreach (var token in tokens)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return token;
            }
        }
    }

    /// <summary>A chat streamer that emits one token and then faults.</summary>
    internal sealed class FailingChatStreamer : IChatCompletionStreamer
    {
        public async IAsyncEnumerable<string> StreamAsync(
            string systemPrompt,
            string userPrompt,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            yield return "partial";

            throw new HttpRequestException("The chat model went away mid-stream.");
        }
    }

    /// <summary>An upload policy with fixed limits and an explicit extension allow-list.</summary>
    internal sealed class StubUploadPolicy : IUploadPolicy
    {
        public StubUploadPolicy(long maxBytes = 1024, params string[] extensions)
        {
            MaxUploadBytes = maxBytes;
            SupportedExtensions = extensions.Length > 0 ? extensions : [".pdf", ".docx", ".txt"];
        }

        public IReadOnlyCollection<string> SupportedExtensions { get; }

        public long MaxUploadBytes { get; }

        public bool IsSupported(string extension) =>
            SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>An in-memory file store that records what was written and deleted.</summary>
    internal sealed class InMemoryFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public InMemoryFileStorage() => UploadRoot = Path.Combine(Path.GetTempPath(), "portion-tests", Guid.NewGuid().ToString("N"));

        public List<string> Deleted { get; } = [];

        public string UploadRoot { get; }

        public Task<StoredFile> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);

            var safeName = Path.GetFileName(fileName);
            var absolutePath = Path.Combine(UploadRoot, safeName);

            _files[absolutePath] = buffer.ToArray();

            return Task.FromResult(new StoredFile(absolutePath, safeName));
        }

        public Task DeleteAsync(string absolutePath, CancellationToken cancellationToken = default)
        {
            _files.Remove(absolutePath);
            Deleted.Add(absolutePath);

            return Task.CompletedTask;
        }

        public bool IsWithinUploadRoot(string absolutePath) =>
            absolutePath.StartsWith(UploadRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A deterministic clock.</summary>
    internal sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    /// <summary>A hasher that returns a caller-supplied digest.</summary>
    internal sealed class StubHashCalculator(string digest) : IHashCalculator
    {
        public string ComputeSha256(ReadOnlySpan<byte> content) => digest;

        public Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(digest);
    }

    /// <summary>An ingestion queue that records what was enqueued.</summary>
    internal sealed class RecordingIngestionQueue : IIngestionQueue
    {
        public List<IngestionRequest> Enqueued { get; } = [];

        public int Count => Enqueued.Count;

        public ValueTask EnqueueAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(request);

            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<IngestionRequest> DequeueAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var request in Enqueued)
            {
                yield return request;
            }

            await Task.CompletedTask;
        }

        public void Complete()
        {
        }
    }
}
