using System.Reflection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Portion.Api.Infrastructure;

/// <summary>OpenAPI document configuration.</summary>
public static class PortionOpenApi
{
    /// <summary>Document name referenced by the UI middleware.</summary>
    public const string DocumentName = "v1";

    /// <summary>Adds the OpenAPI generator and its schema/operation conventions.</summary>
    public static IServiceCollection AddPortionOpenApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddEndpointsApiExplorer()
                       .AddSwaggerGen(options =>
                       {
                           options.SwaggerDoc(DocumentName, new OpenApiInfo
                           {
                               Version = "v1",
                               Title = "Portion.Recruitment.Api",
                               Description =
                                   "Resume ingestion, repository reconciliation and grounded candidate screening. " +
                                   "Errors are returned as RFC 7807 application/problem+json documents."
                           });

                           options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
                           {
                               Name = "Authorization",
                               Type = SecuritySchemeType.Http,
                               Scheme = "bearer",
                               BearerFormat = "JWT",
                               In = ParameterLocation.Header,
                               Description = "Bearer token issued by the identity provider fronting the API."
                           });

                           options.OperationFilter<BearerSecurityRequirementFilter>();
            options.OperationFilter<ResumeUploadOpenApiFilter>();
            options.DocumentFilter<HealthProbeDocumentFilter>();

                           options.CustomSchemaIds(type => type.FullName?.Replace('+', '.') ?? type.Name);

                           // XML comments are the source of the endpoint descriptions above, so the
                           // document is not generated when the file is missing.
                           var xmlPath = TryGetXmlDocumentationPath(Assembly.GetExecutingAssembly());

                           if (File.Exists(xmlPath))
                           {
                               options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
                           }
                       });
    }

    /// <summary>
    /// Adds the three health probes to the document.
    /// </summary>
    /// <remarks>
    /// <c>MapHealthChecks</c> builds its endpoints through a lower-level route builder that the API
    /// explorer does not recognise, so they are routed and reachable but never reach Swashbuckle. A
    /// published contract that silently omits three live endpoints is worse than a small amount of
    /// duplication, so the operations are described here instead. The payload schema mirrors the
    /// anonymous object written by <c>HealthEndpoints</c>.
    /// </remarks>
    private sealed class HealthProbeDocumentFilter : IDocumentFilter
    {
        private const string Tag = "Health";
        private const string SchemaName = "HealthReport";

        public void Apply(OpenApiDocument document, DocumentFilterContext context)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(context);

            document.Components ??= new OpenApiComponents();
            document.Components.Schemas ??= new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);
            document.Components.Schemas[SchemaName] = ReportSchema();
            document.Tags ??= [];

            if (!document.Tags.Any(tag => string.Equals(tag.Name, Tag, StringComparison.Ordinal)))
            {
                document.Tags.Add(new OpenApiTag { Name = Tag, Description = "Liveness and readiness probes." });
            }

            AddProbe(document, "/health", "Health", "Aggregate health report. Identical to the readiness probe.");
            AddProbe(document, "/health/live", "HealthLive", "Liveness probe. Fails only if the process itself cannot serve requests.");
            AddProbe(document, "/health/ready", "HealthReady", "Readiness probe. Includes the database check and reports Ollama degradation.");
        }

        private static void AddProbe(OpenApiDocument document, string path, string operationId, string summary)
        {
            // Probes deliberately carry no security requirement: a load balancer cannot present a
            // bearer token, and requiring one would leave the probes permanently red.
            var responses = new OpenApiResponses
            {
                ["200"] = ProbeResponse("The probe passed. A degraded Ollama still reports 200."),
                ["503"] = ProbeResponse("The probe failed.")
            };

            document.Paths ??= new OpenApiPaths();
            document.Paths[path] = new OpenApiPathItem
            {
                Operations = new Dictionary<OperationType, OpenApiOperation>
                {
                    [OperationType.Get] = new()
                    {
                        Tags = [new OpenApiTag { Name = Tag }],
                        Summary = summary,
                        OperationId = operationId,
                        Responses = responses
                    }
                }
            };
        }

        private static OpenApiResponse ProbeResponse(string description) => new()
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = new OpenApiSchema { Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = SchemaName } }
                }
            }
        };

        private static OpenApiSchema ReportSchema() => new()
        {
            Type = "object",
            Required = new HashSet<string> { "status", "totalDurationMs", "checks" },
            Properties = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
            {
                ["status"] = new() { Type = "string", Description = "Aggregate status: Healthy, Degraded or Unhealthy." },
                ["totalDurationMs"] = new() { Type = "number", Format = "double" },
                ["checks"] = new()
                {
                    Type = "array",
                    Items = new OpenApiSchema
                    {
                        Type = "object",
                        Required = new HashSet<string> { "name", "status", "description", "durationMs" },
                        Properties = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
                        {
                            ["name"] = new() { Type = "string" },
                            ["status"] = new() { Type = "string" },
                            ["description"] = new() { Type = "string", Nullable = true },
                            ["durationMs"] = new() { Type = "number", Format = "double" }
                        }
                    }
                }
            }
        };
    }

    /// <summary>Describes the resume upload as a real <c>multipart/form-data</c> request body.</summary>
    /// <remarks>
    /// Minimal API file uploads cannot be modelled by Swashbuckle out of the box. Left alone, the
    /// inferred <c>file</c> parameter is emitted as a query parameter, which documents the endpoint as
    /// taking a file in the URL — wrong enough to mislead a client generated from this document. The
    /// parameter is therefore replaced by a multipart body carrying a single binary part.
    /// </remarks>
    private sealed class ResumeUploadOpenApiFilter : IOperationFilter
    {
        private const string FileField = "file";

        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(context);

            var relativePath = context.ApiDescription.RelativePath;

            if (!string.Equals(context.ApiDescription.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(relativePath?.Trim('/'), "api/v1/recruitment/resumes", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (operation.Parameters is not null)
            {
                for (var index = operation.Parameters.Count - 1; index >= 0; index--)
                {
                    if (string.Equals(operation.Parameters[index].Name, FileField, StringComparison.OrdinalIgnoreCase))
                    {
                        operation.Parameters.RemoveAt(index);
                    }
                }
            }

            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = "The resume document to ingest.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["multipart/form-data"] = new()
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Required = new HashSet<string> { FileField },
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                [FileField] = new() { Type = "string", Format = "binary" }
                            }
                        }
                    }
                }
            };
        }
    }

    /// <summary>Adds the token requirement to every operation, as the API is deployed behind a gateway.</summary>
    private sealed class BearerSecurityRequirementFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(context);

            // Probes and the Swagger document itself stay anonymous: a load balancer cannot present a
            // bearer token, and requiring one would make the probes permanently red.
            if (string.Equals(context.ApiDescription.RelativePath, "health", StringComparison.OrdinalIgnoreCase) ||
                context.ApiDescription.RelativePath?.StartsWith("health/", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Id = "bearer", Type = ReferenceType.SecurityScheme }
                    }] = Array.Empty<string>()
                }
            ];
        }
    }

    /// <summary>Adds assembly-level XML documentation to the generated document, when present.</summary>
    public static string? TryGetXmlDocumentationPath(Assembly assembly) =>
        Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml");
}
