using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Portion.Api.Configuration;
using Portion.Api.Endpoints;
using Portion.Api.Infrastructure;
using Portion.Infrastructure;
using Portion.Infrastructure.Configuration;

namespace Portion.Api;

/// <summary>
/// Builds and runs the API. Shared verbatim by <c>src/Portion.Api</c> and by the compatibility host
/// in <c>Portion.Server</c>, so the legacy start-up path executes exactly the same pipeline.
/// </summary>
/// <remarks>
/// The content root is pinned to the build output directory rather than left at the process working
/// directory. The application is launched from the repository root by <c>start-all.ps1</c>, from a
/// project directory by <c>dotnet run --project src/Portion.Api</c>, and from a published folder in
/// deployment; pinning the content root makes configuration resolution independent of which of those
/// is used, while all data paths are resolved separately by
/// <c>DataRootResolver</c>.
/// </remarks>
public static class PortionApiHost
{
    /// <summary>Document name and version of the generated OpenAPI document.</summary>
    public const string ApiVersion = "1.0.0";

    /// <summary>Builds the fully configured application without starting it. Used by the tests.</summary>
    public static WebApplication Build(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        ConfigureServices(builder);

        var app = builder.Build();

        ConfigurePipeline(app);

        return app;
    }

    /// <summary>Builds and starts the application.</summary>
    public static void Run(string[] args) => Build(args).Run();

    private static void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddPortionApi(builder.Configuration);
        builder.Services.AddPortionInfrastructure(builder.Configuration);
        builder.Services.AddPortionOpenApi();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddRouting(options => options.LowercaseUrls = true);

        // Response compression is configured explicitly rather than through the defaults helper so
        // the MIME list can omit text/event-stream. A compressing middleware or proxy buffers the
        // stream, which defeats SSE entirely: the first token would be withheld until the whole
        // answer had been produced.
        //
        // AddResponseCompression (not Configure<ResponseCompressionOptions>) is required: the
        // middleware resolves IResponseCompressionProvider from the container, and only this
        // overload registers the provider services.
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes =
            [
                "application/json",
                "application/problem+json",
                "application/octet-stream",
                "text/css",
                "text/html",
                "text/javascript",
                "text/plain"
            ];
        });

        // Kestrel's default 30 MB body ceiling is lower than the configured upload limit, so a
        // legitimate resume would be rejected by the transport before the handler could explain why.
        // Read from configuration rather than from DI because the limit must be known before the
        // host is built.
        var ingestion = new IngestionOptions();
        builder.Configuration.GetSection(IngestionOptions.SectionName).Bind(ingestion);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = ingestion.MaxUploadBytes + ApiServiceCollectionExtensions.MultipartOverheadBytes;

            // The default header advertises Kestrel, which is free reconnaissance.
            kestrel.AddServerHeader = false;
        });
    }

    private static void ConfigurePipeline(WebApplication app)
    {
        // Registered first so it wraps everything below it: a fault raised anywhere else is still
        // rendered as application/problem+json rather than an empty 500.
        app.UseExceptionHandler();

        if (!app.Environment.IsDevelopment())
        {
            // HSTS only in deployed environments. Enabled in Development it would persist a strict
            // transport policy against localhost and break browser testing until the cache is
            // cleared.
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseResponseCompression();

        app.UseForwardedHeaders(BuildForwardedHeaderOptions());

        app.UseCors(CorsOptions.PolicyName);

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", $"Portion.Recruitment.Api v{ApiVersion}");
                options.DocumentTitle = "Portion.Recruitment.Api";
            });
        }

        app.UseMiddleware<RequestLoggingMiddleware>();

        app.MapResumeEndpoints();
        app.MapSyncEndpoints();
        app.MapScreeningEndpoints();
        app.MapHealthEndpoints();
    }

    /// <summary>Builds the forwarded-header options used by the pipeline.</summary>
    private static ForwardedHeadersOptions BuildForwardedHeaderOptions()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,

            // Bounded so a client cannot prepend arbitrary hops and make the middleware walk an
            // attacker-controlled chain.
            ForwardLimit = 2
        };

        // Cleared so the middleware honours the headers from the reverse proxy whatever its address.
        // The default trust list is loopback only, which would silently ignore a proxy in a
        // container. Safe only because the host is deployed behind a trusted proxy; if it is ever
        // exposed directly, populate these lists instead.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        return options;
    }
}
