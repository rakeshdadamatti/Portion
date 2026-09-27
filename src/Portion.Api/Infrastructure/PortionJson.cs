using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portion.Api.Infrastructure;

/// <summary>
/// JSON serialisation settings for the whole API.
/// </summary>
/// <remarks>
/// Enums are serialised by name so that <c>Status</c> is the string <c>"Synced"</c> the frontend
/// contract requires, and so that a reordering of the underlying integers can never silently change
/// the wire format. Screening stage names carry explicit <c>[JsonPropertyName]</c> attributes because
/// that contract is lower-case.
/// </remarks>
public static class PortionJson
{
    /// <summary>Configures the serializer options for the API.</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Converters.Add(new JsonStringEnumConverter());
    }
}
