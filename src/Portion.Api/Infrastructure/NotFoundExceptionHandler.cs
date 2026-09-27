using Microsoft.AspNetCore.Diagnostics;
using Portion.Application.Abstractions;

namespace Portion.Api.Infrastructure;

/// <summary>Renders <see cref="NotFoundException" /> as a 404 ProblemDetails payload.</summary>
public sealed class NotFoundExceptionHandler : IExceptionHandler
{
    private readonly PortionProblemDetailsFactory _problems;
    private readonly ILogger<NotFoundExceptionHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public NotFoundExceptionHandler(PortionProblemDetailsFactory problems, ILogger<NotFoundExceptionHandler> logger)
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

        if (exception is not NotFoundException notFoundException)
        {
            return false;
        }

        _logger.LogInformation("Resource not found: {NotFoundMessage}", notFoundException.Message);

        var problem = _problems.Create(
            httpContext,
            Domain.Common.Error.NotFound(notFoundException.Code, notFoundException.Message));

        await _problems.WriteAsync(httpContext, problem).ConfigureAwait(false);

        return true;
    }
}
