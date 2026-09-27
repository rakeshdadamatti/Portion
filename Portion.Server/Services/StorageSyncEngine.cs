using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portion.Server.Configuration;
using Portion.Server.Data;
using Portion.Server.Entities;
using Portion.Server.Infrastructure;
using Portion.Server.Services.Abstractions;
using Portion.Server.Utilities;

namespace Portion.Server.Services
{
    public class StorageSyncEngine : IStorageSyncEngine
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IngestionChannel _channel;
        private readonly ILogger<StorageSyncEngine> _logger;
        private readonly IngestionOptions _options;

        public StorageSyncEngine(
            IServiceProvider serviceProvider,
            IngestionChannel channel,
            ILogger<StorageSyncEngine> logger,
            IOptions<IngestionOptions> options)
        {
            _serviceProvider = serviceProvider;
            _channel = channel;
            _logger = logger;
            _options = options.Value;
        }

        public async Task ReconcileRepositoryAsync(string directoryPath, CancellationToken token)
        {
            if (!Directory.Exists(directoryPath))
            {
                _logger.LogWarning("Reconciliation skipped — directory not found: {Path}", directoryPath);
                return;
            }

            var filePaths = Directory
                .EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => _options.SupportedExtensions.Contains(
                    Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            foreach (var path in filePaths)
            {
                token.ThrowIfCancellationRequested();
                string hash = ComputeFileHash(path);

                var existing = await dbContext.Resumes.FirstOrDefaultAsync(r => r.FileHash == hash, token);
                if (existing?.Status == IngestionStatus.Synced) continue;

                if (existing is null)
                {
                    existing = new Resume
                    {
                        Id = Guid.NewGuid(),
                        CandidateName = Path.GetFileNameWithoutExtension(path),
                        FilePath = path,
                        FileHash = hash,
                        Status = IngestionStatus.Discovered
                    };
                    dbContext.Resumes.Add(existing);
                    await dbContext.SaveChangesAsync(token);
                }

                existing.Status = IngestionStatus.Processing;
                await dbContext.SaveChangesAsync(token);
                await _channel.Writer.WriteAsync(new IngestionTask(existing.Id, path), token);
            }
        }

        public string ComputeFileHash(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            return Convert.ToHexString(sha256.ComputeHash(stream));
        }
    }
}
