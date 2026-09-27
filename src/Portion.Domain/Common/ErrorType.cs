namespace Portion.Domain.Common;

/// <summary>
/// Classifies the nature of a domain failure so transport layers can map it to a status code
/// without inspecting error codes or messages.
/// </summary>
public enum ErrorType
{
    /// <summary>No failure. Sentinel used by <see cref="Error.None" />.</summary>
    None = 0,

    /// <summary>The caller supplied invalid input. Maps to HTTP 400.</summary>
    Validation,

    /// <summary>The requested resource does not exist. Maps to HTTP 404.</summary>
    NotFound,

    /// <summary>The request conflicts with the current state of the resource. Maps to HTTP 409.</summary>
    Conflict,

    /// <summary>The payload is syntactically valid but too large. Maps to HTTP 413.</summary>
    PayloadTooLarge,

    /// <summary>The caller exceeded a rate limit. Maps to HTTP 429.</summary>
    RateLimited,

    /// <summary>An infrastructure or application fault that is not attributable to the caller. Maps to HTTP 500.</summary>
    Failure
}
