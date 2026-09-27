using Microsoft.EntityFrameworkCore;
using Portion.Application.Abstractions;
using Portion.Domain.Entities;

namespace Portion.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the application. Implements <see cref="IApplicationDbContext" /> so
/// that the application layer depends on an abstraction rather than on this concrete type.
/// </summary>
/// <remarks>
/// The model is configured to produce exactly the column layout of the original hand-created SQLite
/// schema, so the existing <c>portion_local.db</c> keeps working without a migration. The only
/// provider-specific difference is how the embedding vector is stored: a native
/// <c>vector(768)</c> column on PostgreSQL, and a JSON-serialised <c>float[]</c> text column on
/// SQLite.
/// </remarks>
public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    /// <summary>Creates the context.</summary>
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) => ProviderKind = ResolveProviderKind(options);

    /// <inheritdoc />
    public DbSet<Resume> Resumes => Set<Resume>();

    /// <inheritdoc />
    public DbSet<ResumeChunk> ResumeChunks => Set<ResumeChunk>();

    /// <inheritdoc />
    public DatabaseProviderKind ProviderKind { get; }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var isPostgres = ProviderKind == DatabaseProviderKind.Postgres;

        if (isPostgres)
        {
            // Declares the pgvector extension so that `vector` is available to the column mapping.
            modelBuilder.HasPostgresExtension("vector");
        }

        modelBuilder.ApplyConfiguration(new Configurations.ResumeConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.ResumeChunkConfiguration(isPostgres));

        base.OnModelCreating(modelBuilder);
    }

    private static DatabaseProviderKind ResolveProviderKind(DbContextOptions<ApplicationDbContext> options)
    {
        // The provider is recorded in the options extensions that built this context, so it is
        // readable before the DbContext base constructor has initialised the model. Matching on the
        // extension type name keeps this free of a hard reference to either provider package.
        var isPostgres = options.Extensions.Any(e =>
            e.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase));

        return isPostgres ? DatabaseProviderKind.Postgres : DatabaseProviderKind.Sqlite;
    }
}
