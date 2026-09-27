using Microsoft.EntityFrameworkCore;
using Portion.Application.Abstractions;
using Portion.Domain.Common;
using Portion.Domain.Entities;

namespace Portion.Application.Features.Resumes.Commands;

/// <summary>Deletes a resume, optionally removing its file from disk.</summary>
/// <param name="Id">Resume identity.</param>
/// <param name="DeleteFile">When true, also delete the source file — but only if it lives under the managed upload root.</param>
public sealed record DeleteResumeCommand(Guid Id, bool DeleteFile);

/// <summary>Marker returned by a successful delete; the endpoint responds 204 with no body.</summary>
public sealed record DeletedResource;

/// <summary>Handles <see cref="DeleteResumeCommand" />.</summary>
public sealed class DeleteResumeCommandHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ILogger<DeleteResumeCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public DeleteResumeCommandHandler(
        IApplicationDbContext db,
        IFileStorage storage,
        ILogger<DeleteResumeCommandHandler> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the delete. Chunks are removed by the configured cascade. The on-disk file is only
    /// removed when it is inside the managed upload root, so a resume indexed from an external folder
    /// can never cause a file outside the application's control to be deleted.
    /// </summary>
    public async Task<Result<DeletedResource>> HandleAsync(
        DeleteResumeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resume = await _db.Resumes
            .FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (resume is null)
        {
            return Result<DeletedResource>.Failure(Error.NotFound(
                "resumes.not-found",
                $"Resume '{command.Id}' was not found."));
        }

        var filePath = resume.FilePath;
        _db.Resumes.Remove(resume);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (command.DeleteFile && !string.IsNullOrWhiteSpace(filePath))
        {
            if (_storage.IsWithinUploadRoot(filePath))
            {
                await _storage.DeleteAsync(filePath, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Deleted resume {ResumeId} and its managed file.", command.Id);
            }
            else
            {
                _logger.LogInformation(
                    "Deleted resume {ResumeId}; retained file {FilePath} because it is outside the upload root.",
                    command.Id,
                    filePath);
            }
        }

        return Result<DeletedResource>.Success(new DeletedResource());
    }
}
