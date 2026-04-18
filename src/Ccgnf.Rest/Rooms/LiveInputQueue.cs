using System.Collections.Concurrent;
using Ccgnf.Interpreter;

namespace Ccgnf.Rest.Rooms;

/// <summary>
/// A host input queue that accepts live <see cref="Append"/> calls from
/// action POSTs. v1 halts on the synchronous interpreter path — inputs are
/// drained to a snapshot before the interpreter runs, and any appends after
/// that are buffered for a future async-interpreter pass (see
/// <c>docs/plan/steps/06-rooms.md</c> §6c).
/// </summary>
public sealed class LiveInputQueue : IHostInputQueue
{
    private readonly ConcurrentQueue<RtValue> _queue = new();

    public bool IsEmpty => _queue.IsEmpty;

    public int Count => _queue.Count;

    public void Append(RtValue value) => _queue.Enqueue(value);

    public RtValue Next(string prompt)
    {
        if (!_queue.TryDequeue(out var value))
        {
            throw new InvalidOperationException(
                $"Live input queue empty while requesting '{prompt}'. " +
                "The v1 interpreter halts synchronously — add inputs before calling Run.");
        }
        return value;
    }

    /// <summary>
    /// Snapshot the current queue contents as a pre-sequenced
    /// <see cref="QueuedInputs"/>. Used to bridge live-appended inputs into
    /// the synchronous interpreter path: call once before Run.
    /// </summary>
    public QueuedInputs Drain()
    {
        var items = new List<RtValue>();
        while (_queue.TryDequeue(out var v)) items.Add(v);
        return new QueuedInputs(items);
    }
}
