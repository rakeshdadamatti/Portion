using System;
using System.Collections.Generic;

namespace Portion.Server.Entities
{
    public enum IngestionStatus
    {
        Discovered,
        Processing,
        Synced,
        Failed
    }

    public class Resume
    {
        public Guid Id { get; set; }
        public string CandidateName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileHash { get; set; } = string.Empty;
        public IngestionStatus Status { get; set; } = IngestionStatus.Discovered;
        public string? FailureReason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
        public ICollection<ResumeChunk> Chunks { get; set; } = new List<ResumeChunk>();
    }

    public class ResumeChunk
    {
        public Guid Id { get; set; }
        public Guid ResumeId { get; set; }
        public Resume Resume { get; set; } = null!;
        public string TextContent { get; set; } = string.Empty;
        public Pgvector.Vector? Embedding { get; set; }
        public int ChunkIndex { get; set; }
    }
}
