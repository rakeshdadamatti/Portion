namespace Portion.Domain.Common;

/// <summary>
/// An immutable, transport-agnostic description of a failure.
/// </summary>
public sealed record Error
{
    private static readonly IReadOnlyDictionary<string, string[]> EmptyFieldErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    private Error(
        string code,
        string description,
        ErrorType type,
        IReadOnlyDictionary<string, string[]>? validationErrors)
    {
        Code = code;
        Description = description;
        Type = type;
        ValidationErrors = validationErrors is { Count: > 0 } ? validationErrors : null;
    }

    /// <summary>Sentinel representing the absence of an error.</summary>
    public static Error None { get; } = new(string.Empty, string.Empty, ErrorType.None, null);

    /// <summary>Stable machine-readable code, e.g. <c>resumes.not-found</c>.</summary>
    public string Code { get; }

    /// <summary>Human-readable explanation. Never contains stack traces or infrastructure detail.</summary>
    public string Description { get; }

    /// <summary>Failure category used for status-code mapping.</summary>
    public ErrorType Type { get; }

    /// <summary>Optional per-field messages, surfaced as the RFC 7807 <c>errors</c> member.</summary>
    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }

    /// <summary>True when this instance represents a failure.</summary>
    public bool IsFailure => Type != ErrorType.None;

    /// <summary>True when this instance represents the absence of a failure.</summary>
    public bool IsNone => Type == ErrorType.None;

    /// <summary>Creates a 404-mapped error.</summary>
    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound, null);

    /// <summary>Creates a 400-mapped error, optionally carrying per-field messages.</summary>
    public static Error Validation(
        string code,
        string description,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(code, description, ErrorType.Validation, fieldErrors);

    /// <summary>Creates a 409-mapped error.</summary>
    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict, null);

    /// <summary>Creates a 413-mapped error.</summary>
    public static Error PayloadTooLarge(string code, string description) =>
        new(code, description, ErrorType.PayloadTooLarge, null);

    /// <summary>Creates a 429-mapped error.</summary>
    public static Error RateLimited(string code, string description) =>
        new(code, description, ErrorType.RateLimited, null);

    /// <summary>Creates a 500-mapped error. The description must be caller-safe.</summary>
    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure, null);

    /// <summary>Returns the validation errors, or an empty map when there are none.</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors =>
        ValidationErrors ?? EmptyFieldErrors;

    public override string ToString() => IsNone ? "Error.None" : $"{Type}:{Code} - {Description}";
}
