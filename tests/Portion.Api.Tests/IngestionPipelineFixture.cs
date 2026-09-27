using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Portion.Application.Abstractions;
using Portion.Domain.Enums;
using Portion.Infrastructure;
using Portion.Infrastructure.Ingestion;
using Portion.Infrastructure.Persistence;

namespace Portion.Api.Tests;

/// <summary>
/// A host that keeps the production background workers running, so the ingestion pipeline can be
/// exercised end to end.
/// </summary>
/// <remarks>
/// The only substitution is <see cref="IEmbeddingGenerator" />: it is replaced with a deterministic
/// stub so the tests do not require a locally running Ollama. The database initializer, the recovery
/// service, the ingestion worker and the repository sync worker are all the production registrations
/// — that is the surface under test.
/// </remarks>
public sealed class IngestionPipelineFixture : WebApplicationFactory<Program>
{
    public string DataRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        "portion-ingestion-tests",
        Guid.NewGuid().ToString("N"));

    public IngestionRecoveryService? Recovery { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(Path.Combine(DataRoot, "UploadedResumes"));

        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "Sqlite",
                ["Storage:AppDataRoot"] = DataRoot,
                ["Storage:UploadDirectory"] = "UploadedResumes",
                // A file-backed database, unlike the shared in-memory connection, can back the
                // separate DbContext instances the worker creates per item.
                ["ConnectionStrings:DefaultConnection"] = "Data Source=portion_ingestion_test.db",
                ["Ollama:BaseUrl"] = "http://127.0.0.1:1",
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173"
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmbeddingGenerator>();
            services.AddSingleton<IEmbeddingGenerator, DeterministicEmbeddingGenerator>();
        });
    }

    /// <summary>
    /// Builds a container from the production registrations without starting a web host, so the
    /// composition root itself can be asserted on.
    /// </summary>
    public ServiceProvider BuildProductionContainer()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "Sqlite",
                ["Storage:AppDataRoot"] = DataRoot,
                ["ConnectionStrings:DefaultConnection"] = "Data Source=portion_composition_test.db",
                ["Ollama:BaseUrl"] = "http://127.0.0.1:1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPortionInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>Runs the recovery scan on demand, the way the host would at start-up.</summary>
    public async Task RunRecoveryAsync()
    {
        Recovery ??= Services.GetService<IngestionRecoveryService>();

        Assert.NotNull(Recovery);

        await Recovery!.StartAsync(CancellationToken.None);
    }

    /// <summary>Polls a resume until it leaves the in-flight states, or the timeout elapses.</summary>
    public async Task<IngestionStatus> WaitForStatusAsync(Guid resumeId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = IngestionStatus.Discovered;

        while (DateTime.UtcNow < deadline)
        {
            last = await ReadStatusAsync(resumeId);

            if (last is IngestionStatus.Synced or IngestionStatus.Failed)
            {
                return last;
            }

            await Task.Delay(50);
        }

        return last;
    }

    public Task<IngestionStatus> ReadStatusAsync(Guid resumeId) =>
        WithDbAsync(async db => await db.Resumes.AsNoTracking()
            .Where(r => r.Id == resumeId)
            .Select(r => r.Status)
            .FirstOrDefaultAsync());

    public Task<int> ReadChunkCountAsync(Guid resumeId) =>
        WithDbAsync(async db => await db.ResumeChunks.AsNoTracking().CountAsync(c => c.ResumeId == resumeId));

    public Task<Domain.Entities.Resume?> ReadResumeAsync(Guid resumeId) =>
        WithDbAsync(async db => await db.Resumes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == resumeId));

    private async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await action(db);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            if (Directory.Exists(DataRoot))
            {
                Directory.Delete(DataRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file handle left open by a failed test must not cascade.
        }
    }
}

/// <summary>
/// Produces a stable pseudo-embedding from the text so retrieval order is reproducible without a
/// model. Not a semantic embedding — sufficient to exercise the storage and ranking plumbing.
/// </summary>
internal sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator
{
    private const int Dimensions = 768;

    public Task<float[]?> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var vector = new float[Dimensions];

        for (var i = 0; i < text.Length; i++)
        {
            vector[i % Dimensions] += text[i];
        }

        return Task.FromResult<float[]?>(vector);
    }
}
