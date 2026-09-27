namespace Portion.Server.Configuration
{
    public class OllamaOptions
    {
        public const string SectionName = "Ollama";

        public string BaseUrl { get; set; } = "http://localhost:11434";
        public string Model { get; set; } = "gemma4:12b";
        public string EmbeddingModel { get; set; } = "nomic-embed-text";
        public int TimeoutSeconds { get; set; } = 0;
    }
}
