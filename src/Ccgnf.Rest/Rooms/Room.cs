using System.Collections.Concurrent;
using System.Security.Cryptography;
using Ccgnf.Ast;
using Ccgnf.Interpreter;
using InterpreterRt = Ccgnf.Interpreter.Interpreter;

namespace Ccgnf.Rest.Rooms;

public enum RoomLifecycle
{
    WaitingForPlayers,
    Active,
    Finished,
}

public sealed class RoomPlayer
{
    public int PlayerId { get; init; }
    public string Name { get; init; } = "";
    public string Token { get; init; } = "";
    public DateTimeOffset JoinedAt { get; init; }
    public string? DeckName { get; init; }
    public IReadOnlyList<string>? DeckCardNames { get; init; }
}

/// <summary>
/// Server-authoritative room. Holds the loaded <see cref="AstFile"/>, the
/// interpreter's <see cref="GameState"/> once the game starts, the player
/// roster with per-player tokens, and the SSE broadcaster that fans events
/// out to connected subscribers. A per-room lock serialises join / action
/// / lifecycle transitions.
///
/// Since 7f the interpreter runs as a generator — <see cref="InterpreterRun"/>
/// exposes pending inputs via <c>WaitPending</c> and resumes on <c>Submit</c>.
/// A per-room driver task pumps that loop: it consumes buffered submissions
/// (deck names queued at start, then action values arriving via
/// <see cref="AppendAction"/>) and blocks on an internal submission queue
/// when the interpreter needs a value that hasn't been supplied yet.
/// </summary>
public sealed class Room : IDisposable
{
    private readonly object _lock = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly BlockingCollection<PendingSubmission> _submissions = new();
    private InterpreterRun? _run;
    private Task? _driverTask;
    private CancellationTokenSource? _driverCts;
    private bool _disposed;

    public string Id { get; }
    public AstFile AstFile { get; }
    public int Seed { get; }
    public int PlayerSlots { get; }
    public int DeckSize { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public RoomLifecycle Lifecycle { get; private set; } = RoomLifecycle.WaitingForPlayers;
    public GameState? State => _run?.State;
    public SseBroadcaster Broadcaster { get; } = new();
    public IReadOnlyList<RoomPlayer> Players => _players;

    private readonly List<RoomPlayer> _players = new();

    public Room(
        string id,
        AstFile file,
        int seed,
        int playerSlots,
        int deckSize,
        ILoggerFactory loggerFactory)
    {
        Id = id;
        AstFile = file;
        Seed = seed;
        PlayerSlots = playerSlots;
        DeckSize = deckSize;
        CreatedAt = DateTimeOffset.UtcNow;
        LastActivityAt = CreatedAt;
        _loggerFactory = loggerFactory;
    }

    public RoomPlayer? TryJoin(
        string? displayName,
        string? deckName = null,
        IReadOnlyList<string>? deckCardNames = null)
    {
        lock (_lock)
        {
            if (Lifecycle != RoomLifecycle.WaitingForPlayers) return null;
            if (_players.Count >= PlayerSlots) return null;
            int playerId = _players.Count + 1;
            var player = new RoomPlayer
            {
                PlayerId = playerId,
                Name = string.IsNullOrWhiteSpace(displayName) ? $"Player{playerId}" : displayName!.Trim(),
                Token = GenerateToken(),
                JoinedAt = DateTimeOffset.UtcNow,
                DeckName = string.IsNullOrWhiteSpace(deckName) ? null : deckName!.Trim(),
                DeckCardNames = deckCardNames,
            };
            _players.Add(player);
            LastActivityAt = DateTimeOffset.UtcNow;

            Broadcaster.Emit(new RoomEventFrame(
                Step: 0,
                EventType: "PlayerJoined",
                Fields: new Dictionary<string, string>
                {
                    ["playerId"] = playerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["name"] = player.Name,
                }));

            if (_players.Count >= PlayerSlots) StartLocked();
            return player;
        }
    }

    public bool ValidateToken(int playerId, string token)
    {
        lock (_lock)
        {
            var player = _players.FirstOrDefault(p => p.PlayerId == playerId);
            return player is not null && player.Token == token;
        }
    }

    public void AppendAction(int playerId, string action, Dictionary<string, object?>? args)
    {
        lock (_lock)
        {
            LastActivityAt = DateTimeOffset.UtcNow;
            if (_disposed || _submissions.IsAddingCompleted)
            {
                return;
            }
            _submissions.Add(new PendingSubmission(playerId, new RtSymbol(action)));
            Broadcaster.Emit(new RoomEventFrame(
                Step: State?.StepCount is { } step ? (int)step : 0,
                EventType: "ActionAccepted",
                Fields: new Dictionary<string, string>
                {
                    ["playerId"] = playerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["action"] = action,
                }));
            _ = args;
        }
    }

    public void Finish()
    {
        InterpreterRun? run;
        Task? driver;
        CancellationTokenSource? cts;
        lock (_lock)
        {
            if (Lifecycle == RoomLifecycle.Finished) return;
            Lifecycle = RoomLifecycle.Finished;
            LastActivityAt = DateTimeOffset.UtcNow;
            run = _run;
            driver = _driverTask;
            cts = _driverCts;

            if (!_submissions.IsAddingCompleted)
            {
                _submissions.CompleteAdding();
            }

            Broadcaster.Emit(new RoomEventFrame(
                Step: State?.StepCount is { } step ? (int)step : 0,
                EventType: "RoomClosed",
                Fields: new Dictionary<string, string>()));
        }

        try { cts?.Cancel(); } catch { }
        run?.Stop();
        try { driver?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        run?.Dispose();
    }

    public bool IsExpired(TimeSpan ttl, DateTimeOffset now)
    {
        lock (_lock)
        {
            var age = now - LastActivityAt;
            if (Lifecycle == RoomLifecycle.Finished) return age >= ttl;
            if (Lifecycle == RoomLifecycle.WaitingForPlayers && _players.Count == 0) return age >= ttl;
            return false;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Finish();
        _submissions.Dispose();
    }

    private void StartLocked()
    {
        // `_lock` is held by the caller (TryJoin).
        Lifecycle = RoomLifecycle.Active;
        LastActivityAt = DateTimeOffset.UtcNow;

        foreach (var p in _players)
        {
            if (p.DeckCardNames is null) continue;
            foreach (var name in p.DeckCardNames)
            {
                _submissions.Add(new PendingSubmission(p.PlayerId, new RtString(name)));
            }
        }

        InterpreterRun run;
        try
        {
            var interpreter = new InterpreterRt(
                _loggerFactory.CreateLogger<InterpreterRt>(),
                _loggerFactory);
            run = interpreter.StartRun(AstFile, new InterpreterOptions
            {
                Seed = Seed,
                DefaultDeckSize = DeckSize,
                OnEvent = (ev, state) => EmitGameEvent(ev, state),
            });
        }
        catch (Exception ex)
        {
            Broadcaster.Emit(new RoomEventFrame(
                Step: 0,
                EventType: "InterpreterError",
                Fields: new Dictionary<string, string> { ["message"] = ex.Message }));
            Lifecycle = RoomLifecycle.Finished;
            return;
        }

        _run = run;
        _driverCts = new CancellationTokenSource();
        _driverTask = Task.Run(() => DriveRun(run, _driverCts.Token));

        Broadcaster.Emit(new RoomEventFrame(
            Step: 0,
            EventType: "RoomStarted",
            Fields: new Dictionary<string, string>
            {
                ["players"] = _players.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }));
    }

    private void DriveRun(InterpreterRun run, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var pending = run.WaitPending(ct);
                if (pending is null) break;

                Broadcaster.Emit(new RoomEventFrame(
                    Step: (int)run.State.StepCount,
                    EventType: "InputPending",
                    Fields: new Dictionary<string, string>
                    {
                        ["prompt"] = pending.Prompt,
                        ["playerId"] = pending.PlayerId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                        ["options"] = string.Join(",", pending.LegalActions.Select(a => a.Label)),
                    }));

                PendingSubmission submission;
                try
                {
                    submission = _submissions.Take(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (InvalidOperationException) { break; } // CompleteAdding was called

                run.Submit(submission.Value);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }

        var terminal = run.Status switch
        {
            RunStatus.Completed => "RoomFinished",
            RunStatus.Faulted => "InterpreterError",
            RunStatus.Cancelled => "RoomCancelled",
            _ => "RoomHalted",
        };
        var fields = new Dictionary<string, string>
        {
            ["stepCount"] = run.State.StepCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["gameOver"] = run.State.GameOver ? "true" : "false",
            ["status"] = run.Status.ToString(),
        };
        if (run.Fault is { } fault) fields["message"] = fault.Message;
        Broadcaster.Emit(new RoomEventFrame((int)run.State.StepCount, terminal, fields));
    }

    private void EmitGameEvent(GameEvent ev, GameState state)
    {
        var fields = new Dictionary<string, string>
        {
            ["eventType"] = ev.TypeName,
        };
        foreach (var (key, value) in ev.Fields)
        {
            fields["field." + key] = value.ToString() ?? "";
        }
        Broadcaster.Emit(new RoomEventFrame(
            Step: (int)state.StepCount,
            EventType: "GameEvent",
            Fields: fields));
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return "tok_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private readonly record struct PendingSubmission(int? PlayerId, RtValue Value);
}
