using Microsoft.Extensions.Logging;

namespace Ccgnf.Interpreter;

/// <summary>
/// Lifecycle of an <see cref="InterpreterRun"/>. A run walks
/// <c>Running → (WaitingForInput ↔ Running)* → (Completed | Faulted | Cancelled)</c>.
/// </summary>
public enum RunStatus
{
    /// <summary>The interpreter thread is executing; no pending input.</summary>
    Running,
    /// <summary>The interpreter suspended on a Choice; waiting for <see cref="InterpreterRun.Submit"/>.</summary>
    WaitingForInput,
    /// <summary>The event loop drained cleanly.</summary>
    Completed,
    /// <summary>An unhandled exception halted the run; see <see cref="InterpreterRun.Fault"/>.</summary>
    Faulted,
    /// <summary>A caller requested cancellation via <see cref="InterpreterRun.Stop"/>.</summary>
    Cancelled,
}

/// <summary>
/// Cooperative-generator handle for a single interpreter invocation. The
/// interpreter runs on a dedicated task and blocks inside the channel each
/// time a <c>Choice</c> needs input;
/// the handle publishes that blocking point as <see cref="Pending"/> and
/// resumes on <see cref="Submit"/>. The existing synchronous
/// <see cref="Interpreter.Run"/> is kept as a wrapper that drives this handle
/// with a pre-sequenced <see cref="IHostInputQueue"/>.
///
/// Thread model: the handle's public API is safe to call from a single
/// consumer thread (the room lock serializes rest requests). <see cref="State"/>
/// is only safe to observe when <see cref="Status"/> is <c>WaitingForInput</c>,
/// <c>Completed</c>, <c>Faulted</c>, or <c>Cancelled</c> — while the run is
/// <c>Running</c>, the interpreter thread may be mutating it.
/// </summary>
public sealed class InterpreterRun : IDisposable
{
    private readonly Task _task;
    private readonly BlockingInputChannel _channel;
    private readonly CancellationTokenSource _cts;
    private volatile RunStatus _terminalStatus = RunStatus.Running;
    private Exception? _fault;

    /// <summary>
    /// The game state owned by the interpreter. Mutated in place on the
    /// interpreter thread. Safe to read from the consumer thread when
    /// <see cref="Status"/> is not <c>Running</c>.
    /// </summary>
    public GameState State { get; }

    /// <summary>
    /// The run's current status. While the interpreter task is alive, this is
    /// <c>WaitingForInput</c> when the channel holds a published request and
    /// <c>Running</c> otherwise. Once the task exits, <see cref="Status"/>
    /// latches to a terminal value (<c>Completed</c>, <c>Faulted</c>, or
    /// <c>Cancelled</c>) and never moves again.
    /// </summary>
    public RunStatus Status
    {
        get
        {
            var terminal = _terminalStatus;
            if (terminal != RunStatus.Running) return terminal;
            return _channel.CurrentRequest is null ? RunStatus.Running : RunStatus.WaitingForInput;
        }
    }

    public Exception? Fault => _fault;
    public InputRequest? Pending => _channel.CurrentRequest;

    internal InterpreterRun(
        GameState state,
        BlockingInputChannel channel,
        CancellationTokenSource cts,
        Func<BlockingInputChannel, Task> interpreterBody)
    {
        State = state;
        _channel = channel;
        _cts = cts;

        _task = Task.Run(async () =>
        {
            try
            {
                await interpreterBody(channel);
                SetTerminal(RunStatus.Completed, null);
            }
            catch (OperationCanceledException)
            {
                SetTerminal(RunStatus.Cancelled, null);
            }
            catch (Exception ex)
            {
                SetTerminal(RunStatus.Faulted, ex);
            }
            finally
            {
                // Wake any consumer blocked in WaitPending — the loop below
                // will see non-Running status and return the terminal request
                // (null).
                _channel.SignalCompletion();
            }
        });
    }

    private void SetTerminal(RunStatus status, Exception? fault)
    {
        _terminalStatus = status;
        if (fault is not null) _fault = fault;
    }

    /// <summary>
    /// Block until the interpreter either publishes a new pending input or
    /// reaches a terminal status. Returns the pending request; <c>null</c>
    /// means the run ended (inspect <see cref="Status"/>).
    /// </summary>
    public InputRequest? WaitPending(CancellationToken ct = default)
    {
        return _channel.WaitForPending(ct);
    }

    /// <summary>
    /// Supply the host's answer to the current pending input. The interpreter
    /// thread resumes; the next <see cref="WaitPending"/> call yields the
    /// following pending (or a terminal status). No-op if the run already
    /// finished.
    /// </summary>
    public void Submit(RtValue value)
    {
        if (_terminalStatus is RunStatus.Completed or RunStatus.Faulted or RunStatus.Cancelled) return;
        _channel.Submit(value);
    }

    /// <summary>
    /// Legal submissions for <paramref name="playerId"/> at the current
    /// pending. Empty when the run isn't waiting, when there is no pending,
    /// or when the pending is for a different player.
    /// </summary>
    public IReadOnlyList<LegalAction> GetLegalActions(int playerId)
    {
        var req = _channel.CurrentRequest;
        if (req is null) return Array.Empty<LegalAction>();
        if (req.PlayerId is int pid && pid != playerId) return Array.Empty<LegalAction>();
        return req.LegalActions;
    }

    /// <summary>
    /// Cooperatively cancel the run. If the interpreter is blocked in
    /// <see cref="BlockingInputChannel.Next"/>, the cancel token unblocks it
    /// with an <see cref="OperationCanceledException"/>; the task transitions
    /// to <see cref="RunStatus.Cancelled"/>.
    /// </summary>
    public void Stop()
    {
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }
        _channel.Cancel();
    }

    /// <summary>
    /// Wait for the run to reach a terminal status. Useful for tests and for
    /// shutdown paths that need to drain the interpreter thread.
    /// </summary>
    public void WaitForExit(TimeSpan timeout)
    {
        _task.Wait(timeout);
    }

    public void Dispose()
    {
        Stop();
        try { _task.Wait(TimeSpan.FromSeconds(2)); }
        catch { /* surfaced via Status/Fault */ }
        _cts.Dispose();
        _channel.Dispose();
    }
}

/// <summary>
/// The input queue used by <see cref="InterpreterRun"/>. Bridges the
/// synchronous <c>Inputs.Next(request)</c> call on the interpreter thread to
/// the async <c>Submit</c> / <c>WaitPending</c> handshake on the consumer
/// thread. One request is in flight at a time.
/// </summary>
internal sealed class BlockingInputChannel : IHostInputQueue, IDisposable
{
    private readonly object _lock = new();
    private readonly ManualResetEventSlim _requestSet = new(initialState: false);
    private readonly ManualResetEventSlim _responseSet = new(initialState: false);
    private readonly CancellationTokenSource _cts;

    private InputRequest? _current;
    private RtValue? _response;
    private bool _completed;

    public BlockingInputChannel(CancellationTokenSource cts)
    {
        _cts = cts;
    }

    /// <summary>The request the interpreter is currently blocked on, or null.</summary>
    public InputRequest? CurrentRequest
    {
        get { lock (_lock) return _current; }
    }

    public bool IsEmpty => false;

    /// <summary>Interpreter-thread side — blocks until <see cref="Submit"/> is called.</summary>
    public RtValue Next(InputRequest request)
    {
        lock (_lock)
        {
            _current = request;
            _response = null;
            _responseSet.Reset();
        }
        _requestSet.Set();

        _responseSet.Wait(_cts.Token);

        lock (_lock)
        {
            if (_response is null)
            {
                throw new OperationCanceledException("Interpreter input channel cancelled.");
            }
            var value = _response;
            _response = null;
            // _current was cleared by Submit before it signaled; no work here.
            return value;
        }
    }

    /// <summary>Consumer-thread side — blocks until the interpreter either publishes a request or finishes.</summary>
    public InputRequest? WaitForPending(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
        while (true)
        {
            _requestSet.Wait(linked.Token);
            lock (_lock)
            {
                if (_current is not null) return _current;
                if (_completed) return null;
                // Spurious / stale wake — clear and loop while still inside
                // the lock-covered decision window so we don't race a new
                // request being set.
                _requestSet.Reset();
            }
        }
    }

    /// <summary>
    /// Consumer-thread side — supply the answer the interpreter is blocked on.
    /// Clears the pending atomically with setting the response so the next
    /// <see cref="WaitForPending"/> can't observe the stale request.
    /// </summary>
    public void Submit(RtValue value)
    {
        lock (_lock)
        {
            if (_current is null)
            {
                // No pending to answer — drop on the floor. Callers that need
                // strict ordering should check Pending before Submit.
                return;
            }
            _response = value;
            _current = null;
            _requestSet.Reset();
        }
        _responseSet.Set();
    }

    /// <summary>Called by <see cref="InterpreterRun"/> when the interpreter task exits.</summary>
    public void SignalCompletion()
    {
        lock (_lock) _completed = true;
        _requestSet.Set();
        _responseSet.Set();
    }

    public void Cancel()
    {
        SignalCompletion();
    }

    public void Dispose()
    {
        _requestSet.Dispose();
        _responseSet.Dispose();
    }
}
