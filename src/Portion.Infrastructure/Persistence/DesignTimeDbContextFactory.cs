using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Portion.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> construct a context without booting the API host.
/// </summary>
/// <remarks>
/// Read from <c>appsettings.json</c> / environment variables, mirroring the running configuration, so
/// a scaffolded migration targets the same provider and connection string as the application.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string DefaultSqliteConnection = "Data Source=portion_local.db";
    private const string DefaultPostgresConnection =
        "Host=localhost;Port=5433;Database=portion_db;Username=postgres;Password=postgres";

    /// <inheritdoc />
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var basePath = FindContentRoot();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var provider = configuration["DatabaseProvider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? (IsPostgres(provider) ? DefaultPostgresConnection : DefaultSqliteConnection);

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        if (IsPostgres(provider))
        {
            optionsBuilder.UseNpgsql(connectionString, o => o.UseVector());
        }
        else
        {
            optionsBuilder.UseSqlite(connectionString);
        }

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    private static bool IsPostgres(string provider) =>
        provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase);

    private static string FindContentRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "appsettings.json")))
            {
                return directory.FullName;
            }

            var srcMarker = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(srcMarker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
