using Microsoft.Extensions.Options;
using Portion.Server.Configuration;
using Portion.Server.Data;
using Portion.Server.Entities;
using Portion.Server.Infrastructure;
using Portion.Server.Services.Abstractions;
using Portion.Server.Utilities;

namespace Portion.Server.Workers
{
    /// <summary>
    /// Long-running background service that drains the IngestionChannel queue.
    /// For each task it: extracts text → chunks → generates embeddings → saves to DB.
    /// </summary>
    public class ResumeIngestionWorker : BackgroundService
    {
        private readonly IngestionChannel _channel;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ResumeIngestionWorker> _logger;
        private readonly IngestionOptions _options;

        public ResumeIngestionWorker(
            IngestionChannel channel,
            IServiceProvider serviceProvider,
            ILogger<ResumeIngestionWorker> logger,
            IOptions<IngestionOptions> options)
        {
            _channel = channel;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ResumeIngestionWorker started (ChunkSize={Size}, Overlap={Overlap}).",
                _options.ChunkSize, _options.ChunkOverlap);

            await foreach (var task in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessTaskAsync(task, stoppingToken);
            }
        }

        private async Task ProcessTaskAsync(IngestionTask task, CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ollama   = scope.ServiceProvider.GetRequiredService<IOllamaService>();

            try
            {
                var resume = await dbContext.Resumes.FindAsync(new object[] { task.ResumeId }, stoppingToken);
                if (resume is null) return;

                _logger.LogInformation("Processing resume {Id} ({Name}).", resume.Id, resume.CandidateName);

                var text     = TextExtractor.ExtractFromFile(task.FilePath);
                var segments = ChunkText(text, _options.ChunkSize, _options.ChunkOverlap);

                int index = 0;
                foreach (var segment in segments)
                {
                    var embedding = await ollama.GenerateEmbeddingAsync(segment, stoppingToken);
                    dbContext.ResumeChunks.Add(new ResumeChunk
                    {
                        Id          = Guid.NewGuid(),
                        ResumeId    = resume.Id,
                        TextContent = segment,
                        Embedding   = embedding is null ? null : new Pgvector.Vector(embedding),
                        ChunkIndex  = index++
                    });
                }

                resume.Status      = IngestionStatus.Synced;
                resume.LastSyncedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(stoppingToken);

                _logger.LogInformation("Resume {Id} synced — {Chunks} chunk(s).", resume.Id, index);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ingestion failed for task {ResumeId}.", task.ResumeId);

                var resume = await dbContext.Resumes.FindAsync(new object[] { task.ResumeId }, stoppingToken);
                if (resume is not null)
                {
                    resume.Status        = IngestionStatus.Failed;
                    resume.FailureReason = ex.Message;
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
            }
        }

        private List<string> ChunkText(string text, int size, int overlap)
        {
            var words  = text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
            var chunks = new List<string>();
            int step   = Math.Max(size - overlap, 1);

            for (int i = 0; i < words.Length; i += step)
            {
                chunks.Add(string.Join(' ', words.Skip(i).Take(size)));
                if (i + size >= words.Length) break;
            }

            if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(text))
                chunks.Add(text);

            return chunks;
        }
    }
}
