namespace Ccgnf.Interpreter;

/// <summary>
/// FIFO queue of pending events. v1 has a single queue; Interrupt / Debt
/// handling in later versions may add priority lanes (GrammarSpec §8.2).
/// </summary>
public sealed class EventQueue
{
    private readonly Queue<GameEvent> _queue = new();

    public int Count => _queue.Count;

    public void Enqueue(GameEvent e) => _queue.Enqueue(e);

    /// <summary>
    /// Put <paramref name="e"/> at the front of the queue so the next
    /// <see cref="TryDequeue"/> yields it. Used by
    /// <see cref="Interpreter.RunEventLoop"/> when a pre-dispatch halt
    /// predicate fires — the event it inspected hasn't run yet and
    /// deserves its original slot back.
    /// </summary>
    public void EnqueueFront(GameEvent e)
    {
        var existing = _queue.ToArray();
        _queue.Clear();
        _queue.Enqueue(e);
        foreach (var x in existing) _queue.Enqueue(x);
    }

    public bool TryDequeue(out GameEvent e) => _queue.TryDequeue(out e!);

    public IEnumerable<GameEvent> Snapshot() => _queue.ToArray();
}
