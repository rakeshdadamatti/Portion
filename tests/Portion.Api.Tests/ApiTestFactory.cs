using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Portion.Application.Abstractions;
using Portion.Application.Contracts;
using Portion.Domain.Entities;
using Portion.Infrastructure.Persistence;

namespace Portion.Api.Tests;

/// <summary>
/// Boots the real API in memory with an isolated temporary data root.
/// </summary>
/// <remarks>
/// Two substitutions are made, and only two. The database is swapped for a private in-memory SQLite
/// database so the developer's <c>portion_local.db</c> is never touched; and <c>IScreeningService</c>
/// is replaced by a scripted stub so the SSE assertions do not depend on a locally running Ollama.
/// Everything else — routing, model binding, the exception handlers, ProblemDetails serialisation,
/// rate limiting, CORS, the health checks and the ingestion pipeline's real registrations — is the
/// production configuration, because those are precisely the parts under test.
/// </remarks>
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "portion-api-tests",
        Guid.NewGuid().ToString("N"));

    private SqliteConnection? _connection;

    public ScriptedScreeningService Screening { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Directory.CreateDirectory(_dataRoot);

        builder.UseEnvironment(Environments.Development);

        builder.UseSetting("DatabaseProvider", "Sqlite");
        builder.UseSetting("Storage:AppDataRoot", _dataRoot);
        builder.UseSetting("Storage:UploadDirectory", "UploadedResumes");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Data Source=portion_test.db");
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "Sqlite",
                ["Storage:AppDataRoot"] = _dataRoot,
                ["Storage:UploadDirectory"] = "UploadedResumes",
                ["ConnectionStrings:DefaultConnection"] = "Data Source=portion_test.db",
                ["Ollama:BaseUrl"] = "http://127.0.0.1:1"
            }));

        builder.ConfigureServices(services =>
        {
            // A single open connection backs the in-memory database: closing it would drop the store.
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<IApplicationDbContext>();

            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

            // The database host is replaced wholesale, so the hosted initializer is dropped: the
            // schema is created synchronously by CreateDatabaseAsync instead.
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IScreeningService>();
            services.AddScoped<IScreeningService>(_ => Screening);
        });
    }

    /// <summary>
    /// Drops and recreates the schema, giving each test a known-empty database.
    /// </summary>
    /// <remarks>
    /// The in-memory store lives for the lifetime of the factory, which xUnit keeps for a whole test
    /// class. Creating it only once would let one test's seeded rows break another's assertions, so
    /// every test starts from a clean schema.
    /// </remarks>
    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>Runs an action against the test database inside its own scope.</summary>
    public async Task WithDatabaseAsync(Func<ApplicationDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await action(db);
    }

    /// <summary>Seeds a resume and its chunks for list and detail assertions.</summary>
    public Task SeedResumeAsync(string candidateName, int chunkCount = 2)
    {
        var id = Guid.NewGuid();

        return WithDatabaseAsync(async db =>
        {
            db.Resumes.Add(new Domain.Entities.Resume
            {
                Id = id,
                CandidateName = candidateName,
                FilePath = Path.Combine(_dataRoot, "UploadedResumes", $"{candidateName}.txt"),
                FileHash = Guid.NewGuid().ToString("N").ToUpperInvariant(),
                Status = Domain.Enums.IngestionStatus.Synced,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSyncedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });

            for (var index = 0; index < chunkCount; index++)
            {
                db.ResumeChunks.Add(new Domain.Entities.ResumeChunk
                {
                    Id = Guid.NewGuid(),
                    ResumeId = id,
                    TextContent = $"chunk {index}",
                    ChunkIndex = index
                });
            }

            await db.SaveChangesAsync();
        });
    }

    /// <summary>Reads every resume row, untracked, for direct state assertions.</summary>
    public async Task<IReadOnlyList<Resume>> ReadResumesAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Resumes.AsNoTracking().ToListAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        _connection?.Dispose();

        try
        {
            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file handle left open by a failed test must not turn into a cascading test failure.
        }
    }
}

/// <summary>
/// A screening service that replays a fixed event script, so the SSE contract can be asserted
/// without a running model.
/// </summary>
public sealed class ScriptedScreeningService : IScreeningService
{
    /// <summary>The events replayed, in order, for the next call.</summary>
    public List<ScreeningEvent> Script { get; set; } =
    [
        new ScreeningStatusEvent(ScreeningStage.Embedding),
        new ScreeningStatusEvent(ScreeningStage.Search),
        new ScreeningStatusEvent(ScreeningStage.Fallback),
        new ScreeningMatchesEvent(1, [new ResumeMatchCandidate(Guid.NewGuid(), "Ada", 0.5, 1)]),
        new ScreeningStatusEvent(ScreeningStage.Generating),
        new ScreeningTokenEvent("Ada "),
        new ScreeningTokenEvent("worked on the engine.\n"),
        new ScreeningStatusEvent(ScreeningStage.Complete),
        new ScreeningDoneEvent(42)
    ];

    /// <summary>When set, thrown after the script has been replayed.</summary>
    public Exception? Fault { get; set; }

    public string? LastQuery { get; private set; }

    public int? LastTopK { get; private set; }

    public async IAsyncEnumerable<ScreeningEvent> ExecuteAsync(
        string query,
        int topK,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        LastTopK = topK;

        await Task.Yield();

        foreach (var screeningEvent in Script)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return screeningEvent;
        }

        if (Fault is not null)
        {
            throw Fault;
        }
    }
}
