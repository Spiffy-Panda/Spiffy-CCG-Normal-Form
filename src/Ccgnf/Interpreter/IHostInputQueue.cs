namespace Ccgnf.Interpreter;

/// <summary>
/// The channel through which player choices enter the interpreter. Two
/// realities coexist: a pre-sequenced <see cref="QueuedInputs"/> used by tests
/// and the sync <see cref="Interpreter.Run"/> wrapper, and a blocking channel
/// used by <see cref="InterpreterRun"/> so long-lived hosts can drive the
/// interpreter action-by-action. Implementations receive an
/// <see cref="InputRequest"/> so async hosts can surface chooser / legal-action
/// context to the UI; pre-sequenced queues ignore it.
/// </summary>
public interface IHostInputQueue
{
    /// <summary>
    /// Pull the next input for <paramref name="request"/>. Implementations may
    /// ignore the request body; it exists for logging and for async hosts that
    /// need to publish context to a player before blocking.
    /// </summary>
    RtValue Next(InputRequest request);

    /// <summary>True if no more inputs remain (pre-sequenced queues only).</summary>
    bool IsEmpty { get; }
}

/// <summary>
/// Default queue backed by an in-memory list. Consumed in FIFO order;
/// ignores <see cref="InputRequest"/> context.
/// </summary>
public sealed class QueuedInputs : IHostInputQueue
{
    private readonly Queue<RtValue> _queue;

    public QueuedInputs(IEnumerable<RtValue> values)
    {
        _queue = new Queue<RtValue>(values);
    }

    public bool IsEmpty => _queue.Count == 0;

    public RtValue Next(InputRequest request)
    {
        if (_queue.Count == 0)
        {
            throw new InvalidOperationException(
                $"Host input queue exhausted while requesting '{request.Prompt}'.");
        }
        return _queue.Dequeue();
    }
}
