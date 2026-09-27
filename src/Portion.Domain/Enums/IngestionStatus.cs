namespace Portion.Domain.Enums;

/// <summary>
/// Lifecycle of a resume as it moves through the ingestion pipeline.
/// Serialized by name (<c>Discovered</c>, <c>Processing</c>, <c>Synced</c>, <c>Failed</c>).
/// Persisted as an integer, so members must never be reordered.
/// </summary>
public enum IngestionStatus
{
    /// <summary>Found on disk by a folder reconciliation scan; not yet queued.</summary>
    Discovered = 0,

    /// <summary>Queued or actively being extracted, chunked and embedded.</summary>
    Processing = 1,

    /// <summary>Fully indexed; chunks and embeddings are persisted.</summary>
    Synced = 2,

    /// <summary>Ingestion threw; see <see cref="Entities.Resume.FailureReason" />.</summary>
    Failed = 3
}
