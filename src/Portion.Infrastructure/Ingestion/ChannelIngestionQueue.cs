using System.Threading.Channels;
using Portion.Application.Abstractions;

namespace Portion.Infrastructure.Ingestion;

/// <summary>
/// Bounded in-process work queue for resume ingestion.
/// </summary>
/// <remarks>
/// Bounded with <see cref="BoundedChannelFullMode.Wait" /> so a burst of uploads applies backpressure
/// to the request thread instead of exhausting memory, and <c>SingleReader = true</c> because exactly
/// one worker drains it, which lets the channel avoid per-item synchronisation.
/// </remarks>
public sealed class ChannelIngestionQueue : IIngestionQueue
{
    private const int DefaultCapacity = 200;

    private readonly Channel<IngestionRequest> _channel;

    /// <summary>Creates a queue with the default capacity.</summary>
    public ChannelIngestionQueue()
        : this(DefaultCapacity)
    {
    }

    /// <summary>Creates a queue with an explicit capacity.</summary>
    public ChannelIngestionQueue(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 1.");
        }

        _channel = Channel.CreateBounded<IngestionRequest>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public int Count => _channel.Reader.Count;

    /// <inheritdoc />
    public ValueTask EnqueueAsync(IngestionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _channel.Writer.WriteAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<IngestionRequest> DequeueAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    /// <inheritdoc />
    public void Complete() => _channel.Writer.TryComplete();
}
