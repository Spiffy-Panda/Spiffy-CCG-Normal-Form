namespace Ccgnf.Interpreter;

/// <summary>
/// The channel through which player choices enter the interpreter. v1 is
/// strictly pre-sequenced: all inputs are enqueued before a run; the engine
/// pulls them in order and fails fast if more are needed than were provided.
/// Interactive I/O is out of scope until the REST and Godot hosts land.
/// </summary>
public interface IHostInputQueue
{
    /// <summary>
    /// Pull the next input for <paramref name="prompt"/>. Implementations may
    /// ignore the prompt; it exists for logging and future interactive modes.
    /// </summary>
    RtValue Next(string prompt);

    /// <summary>True if no more inputs remain.</summary>
    bool IsEmpty { get; }
}

/// <summary>
/// Default queue backed by an in-memory list. Consumed in FIFO order.
/// </summary>
public sealed class QueuedInputs : IHostInputQueue
{
    private readonly Queue<RtValue> _queue;

    public QueuedInputs(IEnumerable<RtValue> values)
    {
        _queue = new Queue<RtValue>(values);
    }

    public bool IsEmpty => _queue.Count == 0;

    public RtValue Next(string prompt)
    {
        if (_queue.Count == 0)
        {
            throw new InvalidOperationException(
                $"Host input queue exhausted while requesting '{prompt}'.");
        }
        return _queue.Dequeue();
    }
}
