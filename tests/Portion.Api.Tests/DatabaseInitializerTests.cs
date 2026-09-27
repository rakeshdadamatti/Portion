using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Portion.Domain.Entities;
using Portion.Domain.Enums;
using Portion.Infrastructure;
using Portion.Infrastructure.Persistence;

namespace Portion.Api.Tests;

/// <summary>
/// Regression cover for start-up schema creation against a real file-backed SQLite database.
/// </summary>
/// <remarks>
/// The API integration tests substitute the database, so nothing else exercises the production
/// initialisation path — and the first version of it could not bring up a brand-new deployment at
/// all. These tests pin the behaviour that was broken.
/// </remarks>
public class DatabaseInitializerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "portion-init-" + Guid.NewGuid().ToString("N"));

    public DatabaseInitializerTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task CreatesTheSchema_WhenTheDatabaseFileDoesNotExistYet()
    {
        using var provider = BuildProvider();

        // The precondition that broke start-up: EF reports "cannot connect" for a database file that
        // has been created but holds no schema, so an initializer that gates on this can never bring
        // up a fresh deployment.
        using (var before = provider.CreateScope())
        {
            var db = before.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Assert.False(await db.Database.CanConnectAsync());
        }

        await StartAsync(provider);

        using var scope = provider.CreateScope();
        var db2 = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.True(await db2.Database.CanConnectAsync());
        Assert.True(File.Exists(Path.Combine(_root, "portion_local.db")));

        // Both tables exist, so the list endpoint has something to read.
        Assert.Empty(await db2.Resumes.ToListAsync());
    }

    [Fact]
    public async Task LeavesExistingRowsAlone_WhenRunAgainstAnAlreadyInitialisedDatabase()
    {
        using var provider = BuildProvider();

        await StartAsync(provider);

        Guid id;

        using (var seedScope = provider.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            id = Guid.NewGuid();

            db.Resumes.Add(new Resume
            {
                Id = id,
                CandidateName = "Ada Lovelace",
                FilePath = Path.Combine(_root, "UploadedResumes", "Ada Lovelace.txt"),
                FileHash = "ABC123",
                Status = IngestionStatus.Synced
            });

            await db.SaveChangesAsync();
        }

        // Re-running must be a no-op: the checked-in legacy database is a hand-made schema with real
        // data in it, and a second pass must not rebuild or empty it.
        await StartAsync(provider);

        using var verifyScope = provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var stored = await verifyDb.Resumes.SingleOrDefaultAsync();

        Assert.NotNull(stored);
        Assert.Equal(id, stored.Id);
        Assert.Equal("Ada Lovelace", stored.CandidateName);
    }

    private ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "Sqlite",
                ["Storage:AppDataRoot"] = _root,
                ["Storage:UploadDirectory"] = "UploadedResumes",
                ["ConnectionStrings:DefaultConnection"] = "Data Source=portion_local.db",
                ["Ollama:BaseUrl"] = "http://127.0.0.1:1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddPortionInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    private static async Task StartAsync(ServiceProvider provider)
    {
        var initializer = new DatabaseInitializer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.StartAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A handle left open by a failure must not cascade into a further test failure.
        }
    }
}
