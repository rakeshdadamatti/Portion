using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;
using Portion.Server.Configuration;
using Portion.Server.Data;
using Portion.Server.Services.Abstractions;

namespace Portion.Server.Services
{
    public class InteractiveScreeningEngine : IInteractiveScreeningEngine
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IOllamaService _ollama;
        private readonly ILogger<InteractiveScreeningEngine> _logger;
        private readonly IngestionOptions _ingestionOptions;
        private readonly OllamaOptions _ollamaOptions;

        public InteractiveScreeningEngine(
            ApplicationDbContext dbContext,
            IOllamaService ollama,
            ILogger<InteractiveScreeningEngine> logger,
            IOptions<IngestionOptions> ingestionOptions,
            IOptions<OllamaOptions> ollamaOptions)
        {
            _dbContext = dbContext;
            _ollama = ollama;
            _logger = logger;
            _ingestionOptions = ingestionOptions.Value;
            _ollamaOptions = ollamaOptions.Value;
        }

        public async IAsyncEnumerable<string> ExecuteStreamedScreeningAsync(
            string hrQuery,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return "[STATUS] Generating query embedding…";
            var queryVectorArray = await _ollama.GenerateEmbeddingAsync(hrQuery, cancellationToken);

            var contextChunks = new List<string>();

            if (queryVectorArray != null)
            {
                yield return "[STATUS] Searching vector database…";
                var targetVector = new Pgvector.Vector(queryVectorArray);
                var isPostgres = _dbContext.Database.ProviderName?
                    .Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false;

                if (isPostgres)
                {
                    contextChunks = await _dbContext.ResumeChunks
                        .Include(c => c.Resume)
                        .Where(c => c.Embedding != null)
                        .OrderBy(c => c.Embedding!.CosineDistance(targetVector))
                        .Take(_ingestionOptions.TopChunkResults)
                        .Select(c => $"[Candidate: {c.Resume.CandidateName}]\n{c.TextContent}")
                        .ToListAsync(cancellationToken);
                }
                else
                {
                    // In-memory cosine similarity for SQLite (zero-config fallback)
                    var allChunks = await _dbContext.ResumeChunks
                        .Include(c => c.Resume)
                        .Where(c => c.Embedding != null)
                        .ToListAsync(cancellationToken);

                    contextChunks = allChunks
                        .Select(c => new
                        {
                            Chunk = c,
                            Distance = CosineDistance(queryVectorArray, c.Embedding!.ToArray())
                        })
                        .OrderBy(x => x.Distance)
                        .Take(_ingestionOptions.TopChunkResults)
                        .Select(x => $"[Candidate: {x.Chunk.Resume.CandidateName}]\n{x.Chunk.TextContent}")
                        .ToList();
                }
            }

            if (contextChunks.Count == 0)
            {
                yield return "[STATUS] No vector matches found — falling back to keyword search…";
                contextChunks = await _dbContext.ResumeChunks
                    .Include(c => c.Resume)
                    .Where(c => EF.Functions.Like(c.TextContent, $"%{hrQuery}%"))
                    .Take(_ingestionOptions.TopChunkResults)
                    .Select(c => $"[Candidate: {c.Resume.CandidateName}]\n{c.TextContent}")
                    .ToListAsync(cancellationToken);

                if (contextChunks.Count == 0)
                {
                    contextChunks = await _dbContext.ResumeChunks
                        .Include(c => c.Resume)
                        .Take(_ingestionOptions.TopChunkResults)
                        .Select(c => $"[Candidate: {c.Resume.CandidateName}]\n{c.TextContent}")
                        .ToListAsync(cancellationToken);
                }
            }

            yield return $"[STATUS] Found {contextChunks.Count} relevant chunk(s). {_ollamaOptions.Model} is generating response…";

            var compiledContext = contextChunks.Count > 0
                ? string.Join("\n\n---\n\n", contextChunks)
                : "No candidate resumes are currently indexed in the system.";

            const string systemPrompt =
                "You are Portion AI, an elite enterprise recruitment screening agent. Analyze the provided " +
                "candidate resume context to answer the HR recruiter's question. Only use factual data from " +
                "the provided text. If information is missing, clearly state it is not specified. Respond " +
                "using clean, professional markdown with headings, bold text, bullet lists, or tables where applicable.";

            var userPrompt = $"Context Information:\n{compiledContext}\n\nRecruiter Query: {hrQuery}";

            await foreach (var token in _ollama.StreamChatAsync(systemPrompt, userPrompt, cancellationToken))
                yield return token;
        }

        private static float CosineDistance(float[] a, float[] b)
        {
            if (a.Length != b.Length) return 1f;
            float dot = 0f, normA = 0f, normB = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }
            if (normA == 0f || normB == 0f) return 1f;
            return 1f - dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
        }
    }
}
