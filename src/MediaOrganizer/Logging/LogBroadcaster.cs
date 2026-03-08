using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading;

namespace MediaOrganizer.Logging;

public sealed class LogBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<LogEvent>> _subscribers = new();

    // Small tail buffer so new subscribers can see what happened recently.
    private readonly ConcurrentQueue<LogEvent> _recent = new();
    private readonly int _recentMax;
    private int _recentCount;

    public LogBroadcaster(int recentMax = 500)
    {
        _recentMax = Math.Max(0, recentMax);
    }

    public IReadOnlyList<LogEvent> GetRecent(int max)
    {
        if (max <= 0 || _recentMax == 0)
        {
            return Array.Empty<LogEvent>();
        }

        // Snapshot the queue; it's okay if this is slightly inconsistent.
        var snapshot = _recent.ToArray();
        if (snapshot.Length <= max)
            return snapshot;

        return snapshot[^max..];
    }

    public LogSubscription Subscribe(int bufferCapacity = 256)
    {
        var capacity = Math.Clamp(bufferCapacity, 1, 10_000);
        var channel = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var id = Guid.NewGuid();
        _subscribers[id] = channel;

        return new LogSubscription(
            id,
            channel.Reader,
            Unsubscribe: () =>
            {
                if (_subscribers.TryRemove(id, out var removed))
                {
                    removed.Writer.TryComplete();
                }
            });
    }

    public void Publish(LogEvent evt)
    {
        if (_recentMax > 0)
        {
            _recent.Enqueue(evt);
            Interlocked.Increment(ref _recentCount);

            while (Volatile.Read(ref _recentCount) > _recentMax && _recent.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _recentCount);
            }
        }

        foreach (var (_, channel) in _subscribers)
        {
            channel.Writer.TryWrite(evt);
        }
    }

    public readonly record struct LogSubscription(
        Guid Id,
        ChannelReader<LogEvent> Reader,
        Action Unsubscribe);
}
