namespace Portion.Server.Services.Abstractions
{
    public interface IOllamaService
    {
        Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
        IAsyncEnumerable<string> StreamChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
    }
}
