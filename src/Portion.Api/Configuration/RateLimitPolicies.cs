namespace Portion.Api.Configuration;

/// <summary>Rate limiter partition names, referenced by both registration and endpoint policies.</summary>
public static class RateLimitPolicies
{
    /// <summary>Fixed-window budget for the streaming screening endpoint. LLM calls are the most expensive thing this API does.</summary>
    public const string Screening = "screening";

    /// <summary>Fixed-window budget for the ingestion and upload endpoints.</summary>
    public const string Ingestion = "ingestion";

    /// <summary>Name of the per-endpoint policy used by reconciliation and delete operations.</summary>
    public const string Mutations = "mutations";

    /// <summary>Health check endpoints, which a load balancer will poll aggressively.</summary>
    public const string Health = "health";
}
