using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Portion.Api.Configuration;
using Portion.Infrastructure.Configuration;

namespace Portion.Api.Infrastructure;

/// <summary>Composition root for the API layer: cross-cutting concerns only.</summary>
/// <remarks>
/// The API project deliberately owns no business rules. It contributes the HTTP surface, the
/// cross-cutting middleware, and the strongly typed configuration; every feature handler, service and
/// integration is registered by <c>AddPortionInfrastructure</c>.
/// </remarks>
public static class ApiServiceCollectionExtensions
{
    /// <summary>Registers ProblemDetails handlers, CORS, rate limiting, JSON settings, and the upload limits.</summary>
    public static IServiceCollection AddPortionApi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddProblemDetails();
        services.AddExceptionHandler<ValidationExceptionHandler>();
        services.AddExceptionHandler<NotFoundExceptionHandler>();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddSingleton<PortionProblemDetailsFactory>();

        services.ConfigureHttpJsonOptions(options => PortionJson.Configure(options.SerializerOptions));
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => PortionJson.Configure(options.JsonSerializerOptions));

        services.AddPortionApiOptions(configuration);
        services.AddPortionCors(configuration);
        services.AddPortionRateLimiting();
        services.ConfigureUploadLimits();

        return services;
    }

    /// <summary>Binds and validates the API's own options on start.</summary>
    /// <remarks>
    /// <c>ValidateOnStart</c> is the important part: a deployment with an out-of-range limit should
    /// fail the boot rather than discover it on the first oversized request.
    /// </remarks>
    public static IServiceCollection AddPortionApiOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CorsOptions>()
                .Bind(configuration.GetSection(CorsOptions.SectionName))
                .ValidateDataAnnotations()
                .Validate(
                    o => !o.AllowCredentials,
                    "Cors:AllowCredentials cannot be enabled: the API does not use cookies, and credentialed CORS would allow any allowed origin to read authenticated responses.")
                .ValidateOnStart();

        services.AddOptions<ScreeningOptions>()
                .Bind(configuration.GetSection(ScreeningOptions.SectionName))
                .ValidateDataAnnotations()
                .Validate(
                    o => o.DefaultTopK <= o.MaxTopK,
                    "Screening:DefaultTopK must not exceed Screening:MaxTopK.")
                .Validate(
                    o => o.MinPromptLength <= o.MaxPromptLength,
                    "Screening:MinPromptLength must not exceed Screening:MaxPromptLength.")
                .ValidateOnStart();

        return services;
    }

    /// <summary>Adds the explicit-origin CORS policy.</summary>
    public static IServiceCollection AddPortionCors(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new CorsOptions();
        configuration.GetSection(CorsOptions.SectionName).Bind(options);

        return services.AddCors(cors => cors.AddPolicy(
            CorsOptions.PolicyName,
            policy => policy
                .WithOrigins(options.AllowedOrigins)
                .WithMethods("GET", "POST", "DELETE", "OPTIONS")
                .WithHeaders("Accept", "Content-Type", "Authorization", "X-Request-Id", "traceparent")
                .WithExposedHeaders("Content-Disposition")
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10))
                // Credentials stay off: EventSource and fetch cannot send cookies reliably, and the
                // API is authenticated by a bearer token, not by a session. This must also stay
                // consistent with the Cors:AllowCredentials validator in AddPortionApiOptions.
                ));
    }

    /// <summary>
    /// Raises the request and multipart body limits to match the configured upload ceiling.
    /// </summary>
    /// <remarks>
    /// Kestrel's 30 MB default and FormOptions' 128 MB default would otherwise reject a legitimate
    /// resume as a bare 413 before the handler could return a ProblemDetails body explaining why.
    /// A small margin covers multipart framing overhead so a file exactly at the limit still arrives.
    /// </remarks>
    public static IServiceCollection ConfigureUploadLimits(this IServiceCollection services)
    {
        // Resolved through the container, never from a second, ad-hoc service provider.
        services.AddOptions<FormOptions>()
                .Configure<IOptions<IngestionOptions>>((options, ingestion) =>
                {
                    options.MultipartBodyLengthLimit = ingestion.Value.MaxUploadBytes + MultipartOverheadBytes;
                    options.ValueLengthLimit = int.MaxValue;
                    options.MemoryBufferThreshold = 64 * 1024;
                });

        return services;
    }

    /// <summary>Head-room added to the upload limit for multipart framing.</summary>
    public const long MultipartOverheadBytes = 8L * 1024 * 1024;
}
