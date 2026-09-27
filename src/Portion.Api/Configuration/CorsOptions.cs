using System.ComponentModel.DataAnnotations;

namespace Portion.Api.Configuration;

/// <summary>Strongly typed, validated binding of the <c>Cors</c> configuration section.</summary>
/// <remarks>
/// There is deliberately no "allow any origin" option. Credentialed or wildcard CORS is the most
/// common way an otherwise well-built API turns into a data-exfiltration endpoint, so the policy is
/// built from an explicit allow-list and credentials are always disabled.
/// </remarks>
public sealed class CorsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Cors";

    /// <summary>Name of the registered policy.</summary>
    public const string PolicyName = "PortionCorsPolicy";

    /// <summary>Exact origins permitted to call the API.</summary>
    [MinLength(1, ErrorMessage = "Cors:AllowedOrigins must list at least one origin.")]
    public string[] AllowedOrigins { get; set; } = ["http://localhost:5173"];

    /// <summary>Always false. Present so the intent is explicit rather than implied by an absence.</summary>
    public bool AllowCredentials { get; set; }
}
