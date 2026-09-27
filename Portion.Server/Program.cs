using Microsoft.EntityFrameworkCore;
using Portion.Server.Extensions;
using Portion.Server.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ──────────────────────────────────────────────────────────────
builder.Services.AddPortionOptions(builder.Configuration);
builder.Services.AddPortionDatabase(builder.Configuration);
builder.Services.AddPortionServices();
builder.Services.AddPortionCors();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));

// ── Build ──────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Middleware pipeline ────────────────────────────────────────────────────────
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

// ── Database initialisation ────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db         = scope.ServiceProvider.GetRequiredService<Portion.Server.Data.ApplicationDbContext>();
    var dbProvider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
    try
    {
        if (dbProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            db.Database.ExecuteSqlRaw("CREATE EXTENSION IF NOT EXISTS vector;");

        db.Database.EnsureCreated();
        app.Logger.LogInformation("Database initialised — provider: {Provider}", db.Database.ProviderName);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Database initialisation note: {Message}", ex.Message);
    }
}

app.Run();
