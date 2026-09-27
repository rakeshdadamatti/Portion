using Portion.Application.Abstractions;
using Portion.Domain.Enums;

namespace Portion.Application.Contracts;

/// <summary>Resume row as returned by the list endpoint.</summary>
/// <param name="Id">Resume identity.</param>
/// <param name="CandidateName">Display name.</param>
/// <param name="FileName">Derived from <paramref name="FilePath" />; not a stored column.</param>
/// <param name="FilePath">Absolute path on disk.</param>
/// <param name="Status">Pipeline state.</param>
/// <param name="FailureReason">Populated only when <paramref name="Status" /> is <c>Failed</c>.</param>
/// <param name="CreatedAt">Row creation time.</param>
/// <param name="LastSyncedAt">Last successful ingestion time.</param>
/// <param name="ChunkCount">Counted by projection; not a stored column.</param>
/// <param name="EmbeddedChunkCount">Counted by projection; not a stored column.</param>
public sealed record ResumeListItem(
    Guid Id,
    string CandidateName,
    string FileName,
    string FilePath,
    IngestionStatus Status,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime LastSyncedAt,
    int ChunkCount,
    int EmbeddedChunkCount);

/// <summary>A single chunk within a resume detail response.</summary>
/// <param name="Id">Chunk identity.</param>
/// <param name="ChunkIndex">Zero-based position within the resume.</param>
/// <param name="CharacterCount">Length of <paramref name="TextContent" />.</param>
/// <param name="HasEmbedding">Whether a vector was persisted for this chunk.</param>
/// <param name="TextContent">The chunk's plain text.</param>
public sealed record ResumeChunkItem(
    Guid Id,
    int ChunkIndex,
    int CharacterCount,
    bool HasEmbedding,
    string TextContent);

/// <summary>Resume row plus its chunks.</summary>
public sealed record ResumeDetail(
    Guid Id,
    string CandidateName,
    string FileName,
    string FilePath,
    IngestionStatus Status,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime LastSyncedAt,
    int ChunkCount,
    int EmbeddedChunkCount,
    IReadOnlyList<ResumeChunkItem> Chunks);

/// <summary>A page of resume rows plus pagination metadata.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

/// <summary>Response body for an accepted upload.</summary>
public sealed record UploadAcceptedResponse(string Message, Guid ResumeId, bool AlreadyIndexed);

/// <summary>Request body for scheduling a reconciliation scan.</summary>
public sealed record RepositorySyncRequest(string? FolderPath);

/// <summary>Response body for an accepted reconciliation job.</summary>
public sealed record RepositorySyncAcceptedResponse(string Message, Guid JobId);

/// <summary>Polling view of a reconciliation job.</summary>
public sealed record SyncJobResponse(
    Guid JobId,
    string FolderPath,
    SyncJobState State,
    int Discovered,
    int Skipped,
    int Queued,
    int Failed,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error);
