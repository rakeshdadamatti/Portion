using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Portion.Domain.Common;

/// <summary>
/// Lightweight pre-condition helpers used at the edges of the domain. These throw
/// <see cref="ArgumentException" /> rather than domain-specific exceptions so that callers can
/// translate them into a 400 response.
/// </summary>
public static class Guard
{
    /// <summary>Argument pre-condition helpers.</summary>
    public static class Against
    {
        /// <summary>Ensures <paramref name="value" /> is not null.</summary>
        public static T NotNull<T>(
            [NotNull] T? value,
            [CallerArgumentExpression(nameof(value))] string? parameterName = null)
            where T : class
        {
            if (value is null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return value;
        }

        /// <summary>Ensures a string is neither null nor empty.</summary>
        public static string NotNullOrEmpty(
            [NotNull] string? value,
            [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Value must not be null or empty.", parameterName);
            }

            return value;
        }

        /// <summary>Ensures a string is neither null, empty, nor whitespace.</summary>
        public static string NotNullOrWhiteSpace(
            [NotNull] string? value,
            [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value must not be null, empty, or whitespace.", parameterName);
            }

            return value;
        }

        /// <summary>Ensures an integer is greater than or equal to <paramref name="min" />.</summary>
        public static int OutOfRange(
            int value,
            int min,
            int max,
            [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    $"Value must be between {min} and {max} inclusive.");
            }

            return value;
        }
    }
}
