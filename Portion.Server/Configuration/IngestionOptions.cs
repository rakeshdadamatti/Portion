namespace Portion.Server.Configuration
{
    public class IngestionOptions
    {
        public const string SectionName = "Ingestion";

        public int ChunkSize { get; set; } = 500;
        public int ChunkOverlap { get; set; } = 50;
        public string UploadDirectory { get; set; } = "UploadedResumes";
        public string[] SupportedExtensions { get; set; } = [".pdf", ".docx", ".txt"];
        public int TopChunkResults { get; set; } = 5;
    }
}
