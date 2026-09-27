using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Portion.Application.Abstractions;
using Portion.Application.Features.Resumes.Commands;
using Portion.Application.Features.Resumes.Queries;
using Portion.Application.Features.Sync;
using Portion.Application.Services;
using Portion.Infrastructure.Ai;
using Portion.Infrastructure.Configuration;
using Portion.Infrastructure.Documents;
using Portion.Infrastructure.Health;
using Portion.Infrastructure.Ingestion;
using Portion.Infrastructure.Persistence;
using Portion.Infrastructure.Repositories;
using Portion.Infrastructure.Retrieval;
using Portion.Infrastructure.Sourcing;
using Portion.Infrastructure.Storage;
using Portion.Infrastructure.Sync;

namespace Portion.Infrastructure;

/// <summary>
/// Composition root for the infrastructure layer. The API project owns the top-level wiring and calls
/// <see cref="AddPortionInfrastructure" />; nothing outside this project references its types
/// directly, so every implementation detail stays replaceable from one place.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Name of the connection string read from configuration.</summary>
    public const string DefaultConnectionStringName = "DefaultConnection";

    /// <summary>Configuration key selecting the relational provider.</summary>
    public const string ProviderConfigurationKey = "DatabaseProvider";

    private const string SqliteFallbackConnectionString = "Data Source=portion_local.db";

    private const string PostgresFallbackConnectionString =
        "Host=localhost;Port=5433;Database=portion_db;Username=postgres;Password=postgres";

    /// <summary>Registers persistence, integrations, queues, workers, and feature handlers.</summary>
    public static IServiceCollection AddPortionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPortionOptions(configuration);
        services.AddPortionPersistence(configuration);

        // ── Application services (pure logic, no I/O) ─────────────────────────────────────────
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IHashCalculator, DefaultHashCalculator>();
        services.TryAddSingleton<ITextChunker, TextChunker>();
        services.TryAddScoped<IResumeSearchService, ResumeSearchService>();
        services.TryAddScoped<IScreeningService, ScreeningService>();
        services.TryAddScoped<IUnitOfWork, UnitOfWork>();
        services.TryAddScoped<IUploadPolicy>(sp =>
            new OptionsUploadPolicy(sp.GetRequiredService<IOptions<IngestionOptions>>().Value));

        // ── Storage ──────────────────────────────────────────────────────────────────────────
        services.TryAddSingleton(sp => BuildStorageLayout(sp.GetRequiredService<IConfiguration>()));
        services.TryAddSingleton<IFileStorage, FileSystemResumeStorage>();

        // ── Document extraction ──────────────────────────────────────────────────────────────
        services.AddSingleton<IDocumentTextExtractor, PdfTextExtractor>();
        services.AddSingleton<IDocumentTextExtractor, DocxTextExtractor>();
        services.AddSingleton<IDocumentTextExtractor, PlainTextExtractor>();
        services.TryAddSingleton<IDocumentTextExtractorFactory, DocumentTextExtractorFactory>();

        // ── Vector retrieval ─────────────────────────────────────────────────────────────────
        services.TryAddScoped<IVectorChunkSearch, ProviderAwareVectorChunkSearch>();

        // ── AI integrations ─────────────────────────────────────────────────────────────────
        services.AddOllamaClient(configuration);
        services.TryAddTransient<IEmbeddingGenerator, OllamaEmbeddingGenerator>();
        services.TryAddTransient<IChatCompletionStreamer, OllamaChatCompletionStreamer>();

        // ── Queues and background work ───────────────────────────────────────────────────────
        services.TryAddSingleton<IIngestionQueue, ChannelIngestionQueue>();
        services.TryAddSingleton<ISyncJobQueue, ChannelSyncJobQueue>();
        services.TryAddSingleton<ISyncJobTracker, InMemorySyncJobTracker>();

        // AddSingleton, NOT TryAddSingleton: AddPortionPersistence above already registered an
        // IHostedService (DatabaseInitializer), and TryAdd is a no-op once a descriptor for the
        // service type exists. Using TryAdd here silently dropped both workers from the host, which
        // left the ingestion queue undrained and every uploaded resume stranded in Processing with
        // no error anywhere. Hosted services are a multi-registration contract, so the correct
        // operator here is Add.
        services.TryAddSingleton<ResumeIngestionWorker>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ResumeIngestionWorker>());

        services.TryAddSingleton<RepositorySyncWorker>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<RepositorySyncWorker>());

        // Re-queues work that a previous process left in flight. The ingestion queue is in-memory, so
        // a restart discards every queued item; without this, a resume interrupted mid-extraction
        // would remain Processing forever with nothing able to advance it.
        services.TryAddSingleton<IngestionRecoveryService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IngestionRecoveryService>());

        // ── Reconciliation ──────────────────────────────────────────────────────────────────
        services.TryAddScoped<IRepositorySyncService, DirectoryRepositorySyncService>();

        // ── Repositories ────────────────────────────────────────────────────────────────────
        services.TryAddScoped<ResumeRepository>();

        // ── Vertical slice handlers ─────────────────────────────────────────────────────────
        services.TryAddScoped<ListResumesQueryHandler>();
        services.TryAddScoped<GetResumeQueryHandler>();
        services.TryAddScoped<UploadResumeCommandHandler>();
        services.TryAddScoped<DeleteResumeCommandHandler>();
        services.TryAddScoped<ScheduleRepositorySyncCommandHandler>();
        services.TryAddScoped<GetSyncJobQueryHandler>();
        services.TryAddScoped<IResumeIngestionProcessor, ResumeIngestionProcessor>();

        // ── Health checks ───────────────────────────────────────────────────────────────────
        services.AddPortionHealthChecks();

        return services;
    }

    /// <summary>
    /// Binds and validates every options type on start, so a misconfigured deployment fails at
    /// startup with an actionable message instead of at the first request that happens to need it.
    /// </summary>
    public static IServiceCollection AddPortionOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OllamaOptions>()
                .Bind(configuration.GetSection(OllamaOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

        services.AddOptions<IngestionOptions>()
                .Bind(configuration.GetSection(IngestionOptions.SectionName))
                .ValidateDataAnnotations()
                .Validate(
                    o => o.ChunkOverlap < o.ChunkSize,
                    "Ingestion:ChunkOverlap must be smaller than Ingestion:ChunkSize.")
                .ValidateOnStart();

        services.AddOptions<StorageOptions>()
                .Bind(configuration.GetSection(StorageOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers the EF Core context for the configured provider and hosts the schema initializer.
    /// </summary>
    /// <remarks>
    /// No explicit EF logger factory is configured: <c>AddDbContext</c> publishes the application's
    /// service provider, so EF Core resolves <c>ILoggerFactory</c> from the same structured pipeline as
    /// the rest of the application and <c>LogTo</c> would actually <em>reduce</em> fidelity.
    /// </remarks>
    public static IServiceCollection AddPortionPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var usePostgres = IsPostgres(ResolveProvider(configuration));
        var layout = BuildStorageLayout(configuration);

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (usePostgres)
            {
                options.UseNpgsql(ResolvePostgresConnectionString(configuration), npgsql => npgsql.UseVector());
            }
            else
            {
                // A relative SQLite data source is anchored to the resolved data root so the existing
                // local database is reused regardless of the working directory.
                options.UseSqlite(ResolveSqliteConnectionString(configuration, layout));
            }
        });

        services.TryAddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.TryAddSingleton<IHostedService, DatabaseInitializer>();

        return services;
    }

    /// <summary>Registers the shared Ollama HTTP client. Pooling and DNS refresh are handled by the factory.</summary>
    public static IServiceCollection AddOllamaClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;

        services.AddHttpClient(OllamaHttpClient.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;

            client.BaseAddress = options.BaseUri;
            client.Timeout = options.TimeoutSeconds > 0
                ? TimeSpan.FromSeconds(options.TimeoutSeconds)
                : Timeout.InfiniteTimeSpan;
        });

        services.AddHttpClient<OllamaHttpClient>(OllamaHttpClient.HttpClientName);

        return services;
    }

    /// <summary>Registers the liveness and readiness probes.</summary>
    /// <remarks>
    /// Ollama is reported as <see cref="HealthStatus.Degraded" /> rather than
    /// <see cref="HealthStatus.Unhealthy" />: an unavailable local model degrades retrieval but does
    /// not make the API unusable, so taking it out of the load-balancer rotation would be wrong.
    /// </remarks>
    public static IServiceCollection AddPortionHealthChecks(this IServiceCollection services)
    {
        services.AddScoped<DbHealthCheck>();
        services.AddScoped<OllamaHealthCheck>();

        services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy("The process is running."), tags: ["live"])
                .AddCheck<DbHealthCheck>("database", HealthStatus.Unhealthy, ["ready", "db"])
                .AddCheck<OllamaHealthCheck>("ollama", HealthStatus.Degraded, ["ready", "ollama"]);

        return services;
    }

    /// <summary>True when configuration selects PostgreSQL.</summary>
    public static bool IsPostgres(string? provider) =>
        provider is not null && provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the configured provider name, defaulting to SQLite.</summary>
    public static string ResolveProvider(IConfiguration configuration) =>
        configuration[ProviderConfigurationKey] ?? "Sqlite";

    private static StorageLayout BuildStorageLayout(IConfiguration configuration)
    {
        var storage = new StorageOptions();
        configuration.GetSection(StorageOptions.SectionName).Bind(storage);

        return new StorageLayout(DataRootResolver.Resolve(storage.AppDataRoot), storage.UploadDirectory);
    }

    private static string ResolvePostgresConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(DefaultConnectionStringName) ?? PostgresFallbackConnectionString;

    private static string ResolveSqliteConnectionString(IConfiguration configuration, StorageLayout layout)
    {
        var configured = configuration.GetConnectionString(DefaultConnectionStringName);

        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = SqliteFallbackConnectionString;
        }

        var builder = new SqliteConnectionStringBuilder(configured);

        if (!string.IsNullOrWhiteSpace(builder.DataSource) && !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = DataRootResolver.Combine(layout.DataRoot, builder.DataSource);
        }

        return builder.ToString();
    }

    /// <summary>Adapts the context to <see cref="IUnitOfWork" />.</summary>
    private sealed class UnitOfWork : IUnitOfWork
    {
        public UnitOfWork(IApplicationDbContext dbContext) => DbContext = dbContext;

        public IApplicationDbContext DbContext { get; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            DbContext.SaveChangesAsync(cancellationToken);
    }
}
