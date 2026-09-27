using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Portion.Infrastructure.Persistence;

/// <summary>
/// Creates or upgrades the application schema at startup, with bounded retry.
/// </summary>
/// <remarks>
/// The decision logic exists to serve two worlds at once:
/// <list type="bullet">
///   <item>
///     A database that has never been created, or one that already carries an
///     <c>__EFMigrationsHistory</c> table, is managed by EF migrations, so schema evolution is
///     versioned and repeatable.
///   </item>
///   <item>
///     A database whose tables were created by hand (the legacy local <c>portion_local.db</c>) has no
///     migration history, so <c>EnsureCreatedAsync</c> is used. It is a no-op against an existing
///     schema, which is exactly what keeps the checked-in developer database usable.
///   </item>
/// </list>
/// Because no migrations are currently present in the assembly, the migration branch is additionally
/// guarded on there actually being migrations to apply; otherwise <c>MigrateAsync</c> would create an
/// empty history table and leave a brand-new database with no tables at all.
/// </remarks>
public sealed class DatabaseInitializer : IHostedService
{
    private const string MigrationsHistoryTable = "__EFMigrationsHistory";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;

    /// <summary>Creates the initializer.</summary>
    public DatabaseInitializer(
        IServiceScopeFactory scopeFactory,
        ILogger<DatabaseInitializer> logger,
        int maxAttempts = 5,
        TimeSpan? retryDelay = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxAttempts = Math.Max(1, maxAttempts);
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(3);
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                await InitializeOnceAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _maxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Database initialisation attempt {Attempt}/{MaxAttempts} failed; retrying in {Delay}s.",
                    attempt,
                    _maxAttempts,
                    _retryDelay.TotalSeconds);

                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Startup must not crash the host: the health checks report an unusable database, which
                // is a far more actionable signal than a process that refuses to start.
                _logger.LogError(
                    ex,
                    "Database initialisation failed after {MaxAttempts} attempt(s). The /health endpoint will report the database as unhealthy until this is resolved.",
                    _maxAttempts);

                return;
            }
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task InitializeOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var isPostgres = db.ProviderKind == Application.Abstractions.DatabaseProviderKind.Postgres;

        // Only PostgreSQL is gated on reachability. A SQLite data source that does not exist yet is
        // exactly the case this method exists to handle, and EF's CanConnect reports false for a
        // database file that has been created but holds no schema — so gating on it would mean a
        // fresh deployment never gets its schema and never recovers. Creating the file is
        // EnsureCreated's job, and it reports a real, actionable error if the path is unusable.
        if (isPostgres && !await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
        {
            // DataSource is the host for Npgsql and the file path for SQLite. It never contains a
            // password, and naming it turns "cannot connect" into something an operator can act on.
            throw new InvalidOperationException(
                $"Cannot connect to the configured database using provider " +
                $"'{db.Database.ProviderName}' at '{db.Database.GetDbConnection().DataSource}'.");
        }

        if (isPostgres)
        {
            // pgvector must exist before a `vector(768)` column can be created. Requires privileges
            // that a managed instance may withhold, in which case the failure surfaces below.
            await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;", cancellationToken)
                          .ConfigureAwait(false);
        }

        var hasHistory = await HasMigrationsHistoryAsync(db, cancellationToken).ConfigureAwait(false);
        var isEmpty = await IsEmptyAsync(db, cancellationToken).ConfigureAwait(false);
        var migrationsAvailable = db.Database.GetMigrations().Any();

        if (migrationsAvailable && (hasHistory || isEmpty))
        {
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Database migrated — provider: {Provider}.",
                db.Database.ProviderName);
            return;
        }

        var created = await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Database ready — provider: {Provider}, data source: {DataSource}, created: {Created}, migrationsApplied: {MigrationsAvailable}.",
            db.Database.ProviderName,
            db.Database.GetDbConnection().DataSource,
            created,
            migrationsAvailable);
    }

    private static async Task<bool> HasMigrationsHistoryAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var sql = db.ProviderKind == Application.Abstractions.DatabaseProviderKind.Postgres
            ? "SELECT to_regclass('public.__EFMigrationsHistory') IS NOT NULL"
            : "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'";

        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result switch
        {
            bool flag => flag,
            long count => count > 0,
            int count => count > 0,
            _ => false
        };
    }

    private static async Task<bool> IsEmptyAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var sql = db.ProviderKind == Application.Abstractions.DatabaseProviderKind.Postgres
            ? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'"
            : "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'";

        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result switch
        {
            long count => count == 0,
            int count => count == 0,
            _ => false
        };
    }
}
