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

    public bool TryDequeue(out GameEvent e) => _queue.TryDequeue(out e!);

    public IEnumerable<GameEvent> Snapshot() => _queue.ToArray();
}
