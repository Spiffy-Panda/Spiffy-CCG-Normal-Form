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
/// v1 note: the interpreter runs synchronously on transition to Active, so
/// the live input queue is drained into a pre-sequenced snapshot before
/// Run and post-run appends buffer but do not drive further steps. The
/// async refactor is tracked as step 6c in the plan.
/// </summary>
public sealed class Room
{
    private readonly object _lock = new();
    private readonly ILoggerFactory _loggerFactory;

    public string Id { get; }
    public AstFile AstFile { get; }
    public int Seed { get; }
    public int PlayerSlots { get; }
    public int DeckSize { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public RoomLifecycle Lifecycle { get; private set; } = RoomLifecycle.WaitingForPlayers;
    public GameState? State { get; private set; }
    public LiveInputQueue Inputs { get; } = new();
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
            Inputs.Append(new RtString(action));
            Broadcaster.Emit(new RoomEventFrame(
                Step: State?.StepCount is { } step ? (int)step : 0,
                EventType: "ActionAccepted",
                Fields: new Dictionary<string, string>
                {
                    ["playerId"] = playerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["action"] = action,
                    ["queued"] = Inputs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }));
            _ = args;
        }
    }

    public void Finish()
    {
        lock (_lock)
        {
            if (Lifecycle == RoomLifecycle.Finished) return;
            Lifecycle = RoomLifecycle.Finished;
            LastActivityAt = DateTimeOffset.UtcNow;
            Broadcaster.Emit(new RoomEventFrame(
                Step: State?.StepCount is { } step ? (int)step : 0,
                EventType: "RoomClosed",
                Fields: new Dictionary<string, string>()));
        }
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

    private void StartLocked()
    {
        // `_lock` is held by the caller (TryJoin).
        Lifecycle = RoomLifecycle.Active;
        LastActivityAt = DateTimeOffset.UtcNow;

        try
        {
            var interpreter = new InterpreterRt(
                _loggerFactory.CreateLogger<InterpreterRt>(),
                _loggerFactory);
            var snapshot = Inputs.Drain();
            State = interpreter.Run(AstFile, new InterpreterOptions
            {
                Seed = Seed,
                Inputs = snapshot,
                DefaultDeckSize = DeckSize,
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

        Broadcaster.Emit(new RoomEventFrame(
            Step: (int)(State?.StepCount ?? 0),
            EventType: "RoomStarted",
            Fields: new Dictionary<string, string>
            {
                ["stepCount"] = (State?.StepCount ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["gameOver"] = (State?.GameOver ?? false) ? "true" : "false",
            }));
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return "tok_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
