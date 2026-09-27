using System.Diagnostics.CodeAnalysis;

namespace Portion.Domain.Common;

/// <summary>
/// A discriminated result: either a value or an <see cref="Error" />. Used instead of exceptions
/// for expected failure paths so that transport layers can render RFC 7807 payloads directly.
/// </summary>
public readonly struct Result
{
    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure reason, or <see cref="Common.Error.None" /> on success.</summary>
    public Error Error { get; }

    /// <summary>Creates a successful result with no payload.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Creates a failed result.</summary>
    public static Result Failure(Error error) =>
        new(false, error is null ? throw new ArgumentNullException(nameof(error)) : error);

    /// <summary>Creates a failed result from a code/description pair.</summary>
    public static Result Failure(ErrorType type, string code, string description) =>
        Failure(type switch
        {
            ErrorType.Validation => Error.Validation(code, description),
            ErrorType.NotFound => Error.NotFound(code, description),
            ErrorType.Conflict => Error.Conflict(code, description),
            ErrorType.PayloadTooLarge => Error.PayloadTooLarge(code, description),
            ErrorType.RateLimited => Error.RateLimited(code, description),
            _ => Error.Failure(code, description)
        });

    /// <summary>Returns this result cast to a typed <see cref="Result{TValue}" />. Only valid on success.</summary>
    public Result<TValue> ToResult<TValue>(TValue value) =>
        IsSuccess ? Result<TValue>.Success(value) : Result<TValue>.Failure(Error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>A discriminated result carrying a value on success.</summary>
public readonly struct Result<TValue>
{
    private readonly TValue? _value;

    private Result(bool isSuccess, TValue? value, Error error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure reason, or <see cref="Common.Error.None" /> on success.</summary>
    public Error Error { get; }

    /// <summary>The success value. Throws when the result is a failure.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failed Result cannot be accessed.");

    /// <summary>Attempts to read the success value without throwing.</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out TValue value)
    {
        value = _value;
        return IsSuccess && value is not null;
    }

    /// <summary>Creates a successful result.</summary>
    public static Result<TValue> Success(TValue value) => new(true, value, Error.None);

    /// <summary>Creates a failed result.</summary>
    public static Result<TValue> Failure(Error error) =>
        new(false, default, error is null ? throw new ArgumentNullException(nameof(error)) : error);

    /// <summary>Creates a failed result from a code/description pair.</summary>
    public static Result<TValue> Failure(ErrorType type, string code, string description) =>
        Failure(type switch
        {
            ErrorType.Validation => Error.Validation(code, description),
            ErrorType.NotFound => Error.NotFound(code, description),
            ErrorType.Conflict => Error.Conflict(code, description),
            ErrorType.PayloadTooLarge => Error.PayloadTooLarge(code, description),
            ErrorType.RateLimited => Error.RateLimited(code, description),
            _ => Error.Failure(code, description)
        });

    /// <summary>Projects the success value, short-circuiting failures.</summary>
    public Result<TOut> Map<TOut>(Func<TValue, TOut> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return IsSuccess ? Result<TOut>.Success(projection(_value!)) : Result<TOut>.Failure(Error);
    }

    public static implicit operator Result<TValue>(Error error) => Failure(error);
}
