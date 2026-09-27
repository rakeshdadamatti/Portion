using System.Threading.Channels;
using Portion.Application.Abstractions;

namespace Portion.Infrastructure.Ingestion;

/// <summary>
/// Bounded queue of reconciliation job identifiers, decoupling the schedule request from the scan.
/// </summary>
/// <remarks>
/// A job identifier crosses the boundary rather than the request itself: the authoritative job state
/// lives in <see cref="ISyncJobTracker" /> so a client can poll it regardless of queue state.
/// </remarks>
public sealed class ChannelSyncJobQueue : ISyncJobQueue
{
    private const int DefaultCapacity = 64;

    private readonly Channel<Guid> _channel;

    /// <summary>Creates the queue with the default capacity.</summary>
    public ChannelSyncJobQueue()
        : this(DefaultCapacity)
    {
    }

    /// <summary>Creates the queue with an explicit capacity.</summary>
    public ChannelSyncJobQueue(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 1.");
        }

        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    /// <inheritdoc />
    public void Complete() => _channel.Writer.TryComplete();
}
