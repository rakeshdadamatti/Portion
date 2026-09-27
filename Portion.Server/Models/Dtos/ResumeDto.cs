namespace Portion.Server.Models.Dtos
{
    public class ResumeDto
    {
        public Guid Id { get; set; }
        public string CandidateName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? FailureReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastSyncedAt { get; set; }
        public int ChunkCount { get; set; }
    }
}
