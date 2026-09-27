using Microsoft.EntityFrameworkCore;
using Portion.Server.Configuration;
using Portion.Server.Data;
using Portion.Server.Infrastructure;
using Portion.Server.Services;
using Portion.Server.Services.Abstractions;
using Portion.Server.Workers;

namespace Portion.Server.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>Registers all strongly-typed options from configuration.</summary>
        public static IServiceCollection AddPortionOptions(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
            services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));
            return services;
        }

        /// <summary>Configures EF Core with the provider specified in config (PostgreSQL or SQLite).</summary>
        public static IServiceCollection AddPortionDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            var dbProvider = configuration["DatabaseProvider"] ?? "Sqlite";

            if (dbProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                var connectionString = configuration.GetConnectionString("DefaultConnection")
                    ?? "Host=localhost;Database=portion_db;Username=postgres;Password=postgres";
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseNpgsql(connectionString, o => o.UseVector()));
            }
            else
            {
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlite("Data Source=portion_local.db"));
            }

            return services;
        }

        /// <summary>Registers all application services, infrastructure, and background workers.</summary>
        public static IServiceCollection AddPortionServices(this IServiceCollection services)
        {
            // Infrastructure
            services.AddSingleton<IngestionChannel>();

            // Services wired to their interfaces for testability
            services.AddSingleton<IStorageSyncEngine, StorageSyncEngine>();
            services.AddHttpClient<IOllamaService, OllamaService>();
            services.AddScoped<IInteractiveScreeningEngine, InteractiveScreeningEngine>();

            // Background pipeline
            services.AddHostedService<ResumeIngestionWorker>();

            return services;
        }

        /// <summary>Adds the CORS policy used by the React frontend.</summary>
        public static IServiceCollection AddPortionCors(this IServiceCollection services)
        {
            services.AddCors(options =>
                options.AddPolicy("AllowAll", policy =>
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod()));
            return services;
        }
    }
}
