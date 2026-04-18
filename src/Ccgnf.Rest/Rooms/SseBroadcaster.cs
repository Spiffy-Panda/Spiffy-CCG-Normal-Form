using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Ccgnf.Rest.Rooms;

/// <summary>
/// Fans out server-sent event frames to connected subscribers. Each
/// subscriber gets an unbounded channel; dropped connections are reaped
/// when the reader signals cancellation. Frames are serialized once and
/// queued per subscriber.
/// </summary>
public sealed class SseBroadcaster : IAsyncDisposable
{
    private readonly List<Subscriber> _subscribers = new();
    private readonly object _lock = new();
    private bool _closed;

    public RoomEventFrame[] Backlog
    {
        get
        {
            lock (_lock) return _backlog.ToArray();
        }
    }

    private readonly List<RoomEventFrame> _backlog = new();

    public IAsyncEnumerable<RoomEventFrame> Subscribe(CancellationToken ct)
    {
        var ch = Channel.CreateUnbounded<RoomEventFrame>();
        var sub = new Subscriber(ch);
        lock (_lock)
        {
            if (_closed)
            {
                ch.Writer.Complete();
                return ch.Reader.ReadAllAsync(ct);
            }
            foreach (var frame in _backlog) ch.Writer.TryWrite(frame);
            _subscribers.Add(sub);
        }
        ct.Register(() =>
        {
            lock (_lock) _subscribers.Remove(sub);
            ch.Writer.TryComplete();
        });
        return ch.Reader.ReadAllAsync(ct);
    }

    public void Emit(RoomEventFrame frame)
    {
        lock (_lock)
        {
            if (_closed) return;
            _backlog.Add(frame);
            foreach (var sub in _subscribers) sub.Channel.Writer.TryWrite(frame);
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Subscriber> closing;
        lock (_lock)
        {
            _closed = true;
            closing = new List<Subscriber>(_subscribers);
            _subscribers.Clear();
        }
        foreach (var sub in closing) sub.Channel.Writer.TryComplete();
        await Task.CompletedTask;
    }

    private sealed record Subscriber(Channel<RoomEventFrame> Channel);
}

public sealed record RoomEventFrame(int Step, string EventType, IReadOnlyDictionary<string, string> Fields)
{
    public string ToSseFrame()
    {
        var payload = JsonSerializer.Serialize(new
        {
            step = Step,
            @event = new { type = EventType, fields = Fields },
        });
        return $"event: game-event\ndata: {payload}\n\n";
    }
}
