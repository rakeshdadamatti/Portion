using Portion.Domain.Common;

namespace Portion.Application.Abstractions;

/// <summary>Raised when input fails domain validation. Rendered as an RFC 7807 400.</summary>
public sealed class ValidationException : Exception
{
    /// <summary>Creates a validation exception with optional per-field messages.</summary>
    public ValidationException(
        string message,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null,
        Exception? innerException = null)
        : base(message, innerException) => FieldErrors = fieldErrors;

    /// <summary>Creates a validation exception from a single error.</summary>
    public ValidationException(Error error)
        : base(error.Description) => FieldErrors = error.ValidationErrors;

    /// <summary>Per-field messages surfaced as the RFC 7807 <c>errors</c> member.</summary>
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }

    /// <summary>Builds a <see cref="ValidationException" /> from a field/message map.</summary>
    public static ValidationException FromFields(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new("One or more validation errors occurred.", fieldErrors);
}

/// <summary>Raised when a requested resource does not exist. Rendered as an RFC 7807 404.</summary>
public sealed class NotFoundException : Exception
{
    /// <summary>Creates a not-found exception.</summary>
    public NotFoundException(string message, string code = "resource.not-found")
        : base(message) => Code = code;

    /// <summary>Creates a not-found exception from a domain error.</summary>
    public NotFoundException(Error error)
        : base(error.Description) => Code = error.Code;

    /// <summary>Stable machine-readable code.</summary>
    public string Code { get; }
}
