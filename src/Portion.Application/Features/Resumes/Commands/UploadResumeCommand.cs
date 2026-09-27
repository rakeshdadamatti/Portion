using Microsoft.EntityFrameworkCore;
using Portion.Application.Abstractions;
using Portion.Application.Contracts;
using Portion.Domain.Common;
using Portion.Domain.Entities;
using Portion.Domain.Enums;

namespace Portion.Application.Features.Resumes.Commands;

/// <summary>Accepts an uploaded resume for ingestion.</summary>
/// <param name="FileName">Client-supplied file name, used to derive the candidate name and to pick an extractor.</param>
/// <param name="Content">The uploaded stream.</param>
/// <param name="Length">Declared length in bytes, checked against the configured upload ceiling.</param>
public sealed record UploadResumeCommand(string FileName, Stream Content, long Length);

/// <summary>Handles <see cref="UploadResumeCommand" />.</summary>
/// <remarks>
/// Validation order is significant and observable: an empty body is a 400, an unsupported extension
/// is a 400, and an oversized body is a 413. The temporary file is always removed when the request
/// is rejected or when the content was already indexed.
/// </remarks>
public sealed class UploadResumeCommandHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IHashCalculator _hasher;
    private readonly IIngestionQueue _queue;
    private readonly IUploadPolicy _policy;
    private readonly IClock _clock;
    private readonly ILogger<UploadResumeCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public UploadResumeCommandHandler(
        IApplicationDbContext db,
        IFileStorage storage,
        IHashCalculator hasher,
        IIngestionQueue queue,
        IUploadPolicy policy,
        IClock clock,
        ILogger<UploadResumeCommandHandler> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Executes the command.</summary>
    public async Task<Result<UploadAcceptedResponse>> HandleAsync(
        UploadResumeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Length <= 0)
        {
            return Result<UploadAcceptedResponse>.Failure(Error.Validation(
                "resumes.upload.empty-file",
                "The uploaded file is empty.",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["file"] = ["No file content was received."]
                }));
        }

        var extension = Path.GetExtension(command.FileName);
        if (!_policy.IsSupported(extension))
        {
            return Result<UploadAcceptedResponse>.Failure(Error.Validation(
                "resumes.upload.unsupported-extension",
                $"'{extension}' is not a supported document type.",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["file"] = [$"Supported types: {string.Join(", ", _policy.SupportedExtensions)}."]
                }));
        }

        if (command.Length > _policy.MaxUploadBytes)
        {
            return Result<UploadAcceptedResponse>.Failure(Error.PayloadTooLarge(
                "resumes.upload.too-large",
                $"The uploaded file exceeds the {_policy.MaxUploadBytes} byte limit."));
        }

        // Write to managed storage first so the content hash can be computed from the bytes that
        // will actually be ingested.
        var stored = await _storage.SaveAsync(command.Content, command.FileName, cancellationToken)
                                     .ConfigureAwait(false);

        try
        {
            var hash = await _hasher.ComputeSha256Async(stored.AbsolutePath, cancellationToken)
                                    .ConfigureAwait(false);

            // Deliberately tracked, not AsNoTracking: when a previously failed row matches this
            // content it is re-used below, and the changes made to it (new path, cleared failure
            // reason) have to be persisted by the SaveChanges call that follows. Reading it
            // untracked would silently discard them.
            var existing = await _db.Resumes
                .FirstOrDefaultAsync(r => r.FileHash == hash, cancellationToken)
                .ConfigureAwait(false);

            if (existing is { Status: IngestionStatus.Synced })
            {
                await _storage.DeleteAsync(stored.AbsolutePath, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Upload {FileName} deduplicated against resume {ResumeId} by hash {HashPrefix}.",
                    command.FileName,
                    existing.Id,
                    hash[..Math.Min(8, hash.Length)]);

                return Result<UploadAcceptedResponse>.Success(new UploadAcceptedResponse(
                    "Resume is already indexed.",
                    existing.Id,
                    AlreadyIndexed: true));
            }

            // Re-use the row of a previously failed attempt so that retries do not accumulate
            // duplicate rows for the same content.
            var resume = existing ?? new Resume
            {
                Id = Guid.NewGuid(),
                CreatedAt = _clock.UtcNow.UtcDateTime,
                LastSyncedAt = _clock.UtcNow.UtcDateTime
            };

            resume.CandidateName = Path.GetFileNameWithoutExtension(command.FileName);
            resume.FilePath = stored.AbsolutePath;
            resume.FileHash = hash;
            resume.Status = IngestionStatus.Processing;
            resume.FailureReason = null;

            if (existing is null)
            {
                _db.Resumes.Add(resume);
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _queue.EnqueueAsync(
                    new IngestionRequest(resume.Id, stored.AbsolutePath, IngestionTrigger.Upload),
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Resume {ResumeId} ({CandidateName}) queued for ingestion from {FileName}.",
                resume.Id,
                resume.CandidateName,
                command.FileName);

            return Result<UploadAcceptedResponse>.Success(new UploadAcceptedResponse(
                "Resume queued for ingestion.",
                resume.Id,
                AlreadyIndexed: false));
        }
        catch
        {
            // Never leave an orphaned file behind when the request fails after the write.
            await _storage.DeleteAsync(stored.AbsolutePath, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>Upload limits and format policy, supplied by the composition root.</summary>
public interface IUploadPolicy
{
    /// <summary>Extensions accepted for ingestion, lower-case and dot-prefixed.</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>Maximum accepted upload size in bytes.</summary>
    long MaxUploadBytes { get; }

    /// <summary>True when the extension is accepted, compared case-insensitively.</summary>
    bool IsSupported(string extension);
}
