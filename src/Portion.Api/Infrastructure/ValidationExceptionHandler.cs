using Microsoft.AspNetCore.Diagnostics;
using Portion.Application.Abstractions;

namespace Portion.Api.Infrastructure;

/// <summary>Renders <see cref="ValidationException" /> as a 400 ProblemDetails payload.</summary>
public sealed class ValidationExceptionHandler : IExceptionHandler
{
    private readonly PortionProblemDetailsFactory _problems;
    private readonly ILogger<ValidationExceptionHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public ValidationExceptionHandler(PortionProblemDetailsFactory problems, ILogger<ValidationExceptionHandler> logger)
    {
        _problems = problems ?? throw new ArgumentNullException(nameof(problems));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not ValidationException validationException)
        {
            return false;
        }

        _logger.LogInformation(
            "Rejected a request with validation errors: {ValidationMessage}",
            validationException.Message);

        var problem = _problems.Create(
            httpContext,
            Domain.Common.Error.Validation(
                "request.validation-failed",
                validationException.Message,
                validationException.FieldErrors));

        await _problems.WriteAsync(httpContext, problem).ConfigureAwait(false);

        return true;
    }
}
