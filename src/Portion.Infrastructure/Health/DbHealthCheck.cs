using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Portion.Infrastructure.Persistence;

namespace Portion.Infrastructure.Health;

/// <summary>
/// Readiness probe for the relational store: proves a connection can be opened and that a trivial
/// round trip succeeds, rather than merely that a connection string parses.
/// </summary>
public sealed class DbHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _db;

    /// <summary>Creates the check. Resolved per health request, so a scoped context is appropriate.</summary>
    public DbHealthCheck(ApplicationDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return HealthCheckResult.Unhealthy(
                    $"The {_db.Database.ProviderName} database could not be reached.");
            }

            await _db.Resumes
                .AsNoTracking()
                .Select(r => r.Id)
                .Take(1)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy(
                $"The {_db.Database.ProviderName} database is reachable and the resume schema is queryable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The database is not usable.", ex);
        }
    }
}
