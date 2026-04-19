using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
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

public enum SeatKind
{
    Human,
    Cpu,
}

public sealed class RoomPlayer
{
    public int PlayerId { get; init; }
    public string Name { get; init; } = "";
    public string Token { get; init; } = "";
    public DateTimeOffset JoinedAt { get; init; }
    public string? DeckName { get; init; }
    public IReadOnlyList<string>? DeckCardNames { get; init; }
    public SeatKind SeatKind { get; init; } = SeatKind.Human;
}

/// <summary>
/// Seat blueprint used to pre-fill CPU seats at room creation time. Humans
/// join via the regular <see cref="Room.TryJoin"/> path; CPUs are installed
/// before any human arrives so <see cref="Room.PlayerSlots"/> + human joins
/// can trigger the usual start transition.
/// </summary>
public sealed record CpuSeatSpec(
    string? Name,
    string? DeckName,
    IReadOnlyList<string>? DeckCardNames);

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
        ILoggerFactory loggerFactory,
        IReadOnlyList<CpuSeatSpec>? cpuSeats = null)
    {
        Id = id;
        AstFile = file;
        Seed = seed;
        PlayerSlots = playerSlots;
        DeckSize = deckSize;
        CreatedAt = DateTimeOffset.UtcNow;
        LastActivityAt = CreatedAt;
        _loggerFactory = loggerFactory;

        if (cpuSeats is not null)
        {
            foreach (var spec in cpuSeats)
            {
                InstallCpuSeatLocked(spec);
            }
            if (_players.Count >= PlayerSlots) StartLocked();
        }
    }

    private void InstallCpuSeatLocked(CpuSeatSpec spec)
    {
        int playerId = _players.Count + 1;
        var player = new RoomPlayer
        {
            PlayerId = playerId,
            Name = string.IsNullOrWhiteSpace(spec.Name) ? $"CPU{playerId}" : spec.Name!.Trim(),
            Token = "cpu", // CPUs don't accept external action POSTs; token unused.
            JoinedAt = DateTimeOffset.UtcNow,
            DeckName = string.IsNullOrWhiteSpace(spec.DeckName) ? null : spec.DeckName!.Trim(),
            DeckCardNames = spec.DeckCardNames,
            SeatKind = SeatKind.Cpu,
        };
        _players.Add(player);
        Broadcaster.Emit(new RoomEventFrame(
            Step: 0,
            EventType: "PlayerJoined",
            Fields: new Dictionary<string, string>
            {
                ["playerId"] = playerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["name"] = player.Name,
                ["seatKind"] = "Cpu",
            }));
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

        // Positional decks: roster order matches state.Players order because
        // StateBuilder iterates `Player[i] for i ∈ {1, 2}` declaratively and
        // roster PlayerId is allocated in the same 1..N sequence. Pre-seated
        // CPUs fill indices 0..N-1 first; humans come after.
        var initialDecks = _players
            .Select(p => p.DeckCardNames)
            .ToList();

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
                InitialDecks = initialDecks,
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
                        // Flat comma-joined label list kept for back-compat with
                        // the v1 action-bar renderer — the richer `legalActions`
                        // field supersedes it.
                        ["options"] = string.Join(",", pending.LegalActions.Select(a => a.Label)),
                        ["legalActions"] = SerializeLegalActions(pending.LegalActions),
                    }));

                // CPU seats act autonomously: pick the first legal action
                // (or a "pass" sentinel when the current Choice happens to
                // have no options exposed). Fed through the same Submit
                // path so human-vs-CPU runs are indistinguishable from
                // human-vs-human with pre-sequenced inputs.
                if (TryResolveCpuSubmission(run, pending, out var cpuSeat, out var cpuValue))
                {
                    Broadcaster.Emit(new RoomEventFrame(
                        Step: (int)run.State.StepCount,
                        EventType: "CpuAction",
                        Fields: new Dictionary<string, string>
                        {
                            ["playerId"] = cpuSeat!.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["value"] = cpuValue.ToString() ?? "",
                        }));
                    run.Submit(cpuValue);
                    continue;
                }

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

        // Promote room lifecycle so the tabletop header reflects the game
        // actually being over. Without this, a GameEnd event flips
        // state.GameOver but the room still reads "Active" in the UI.
        lock (_lock)
        {
            if (Lifecycle == RoomLifecycle.Active)
            {
                Lifecycle = RoomLifecycle.Finished;
                LastActivityAt = DateTimeOffset.UtcNow;
            }
        }

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

    private static readonly JsonSerializerOptions _legalActionJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static string SerializeLegalActions(IReadOnlyList<LegalAction> actions)
    {
        // LegalAction ships kind + label + optional metadata so UIs can render
        // meaningful button labels ("Play Cinderhound (1⚡)" vs "play:57").
        // Serialized as a compact JSON array on the SSE frame.
        var projected = actions.Select(a => new
        {
            a.Kind,
            a.Label,
            Metadata = a.Metadata,
        });
        return JsonSerializer.Serialize(projected, _legalActionJsonOpts);
    }

    private bool TryResolveCpuSubmission(
        InterpreterRun run,
        Ccgnf.Interpreter.InputRequest pending,
        out RoomPlayer? seat,
        out RtValue value)
    {
        seat = null;
        value = new RtSymbol("pass");
        if (pending.PlayerId is not int entityId) return false;

        // pending.PlayerId is a GameState Entity.Id. Map it to roster order
        // (Players list is in declaration order; roster PlayerId 1 = first
        // Player entity allocated, 2 = second).
        int rosterIdx = -1;
        for (int i = 0; i < run.State.Players.Count; i++)
        {
            if (run.State.Players[i].Id == entityId) { rosterIdx = i; break; }
        }
        if (rosterIdx < 0) return false;

        lock (_lock)
        {
            if (rosterIdx >= _players.Count) return false;
            seat = _players[rosterIdx];
        }
        if (seat.SeatKind != SeatKind.Cpu) return false;

        value = ChooseCpuAction(run, pending, entityId);
        return true;
    }

    /// <summary>
    /// Baseline-plus CPU policy. Good enough to exercise Unit-vs-Unit clashes
    /// without a real search / utility system (that's the long-term AI plan).
    /// The ladder, in order:
    ///
    /// 1. If a Clash attack choice is offered, always attack.
    /// 2. If a target-entity choice includes an opponent-owned entity, pick
    ///    the one with the lowest HP counter (integrity / current_ramparts /
    ///    current_hp). Avoids wasting damage on already-collapsed conduits
    ///    and ignores friendly targets that would be self-damage.
    /// 3. If a play-card choice exists, prefer Unit cards over Maneuvers so
    ///    the CPU builds board presence. Tie-break by lowest cost (curve).
    /// 4. For arena picks, prefer an arena where the opponent already has at
    ///    least one Unit (to force Unit-vs-Unit overlap) — otherwise first
    ///    uncollapsed opponent conduit.
    /// 5. Choice options (Mulligan): pass.
    /// 6. Fallback to <c>LegalActions[0]</c>.
    ///
    /// No look-ahead, no scoring of hypothetical states. Deterministic given
    /// the same LegalActions ordering.
    /// </summary>
    private RtValue ChooseCpuAction(
        InterpreterRun run,
        Ccgnf.Interpreter.InputRequest pending,
        int cpuEntityId)
    {
        var actions = pending.LegalActions;
        if (actions.Count == 0) return new RtSymbol("pass");

        // 1. Clash: always attack.
        var attack = actions.FirstOrDefault(
            a => a.Kind == "declare_attacker" && a.Label == "attack");
        if (attack is not null) return new RtSymbol(attack.Label);

        // 2. Target: opponent-owned with lowest HP-ish counter > 0.
        if (actions.Any(a => a.Kind == "target_entity"))
        {
            var pick = PickTargetForCpu(run, actions, cpuEntityId);
            if (pick is not null) return new RtSymbol(pick.Label);
        }

        // 3. Play card: prefer Unit, tie-break by cost ascending.
        var plays = actions.Where(a => a.Kind == "play_card").ToList();
        if (plays.Count > 0)
        {
            int CostOf(LegalAction a) =>
                int.TryParse(a.Metadata?.GetValueOrDefault("cost") ?? "", out var c) ? c : int.MaxValue;
            bool IsUnit(LegalAction a) => IsUnitPlay(run, a);
            var unitPlays = plays.Where(IsUnit).ToList();
            var pool = unitPlays.Count > 0 ? unitPlays : plays;
            var pick = pool.OrderBy(CostOf).First();
            return new RtSymbol(pick.Label);
        }

        // 4. Arena: prefer overlap with opponent unit, else uncollapsed conduit.
        if (actions.Any(a => a.Kind == "target_arena"))
        {
            var pick = PickArenaForCpu(run, actions, cpuEntityId);
            if (pick is not null) return new RtSymbol(pick.Label);
        }

        // 5. Mulligan / generic choice — pass if offered, otherwise first.
        var passChoice = actions.FirstOrDefault(a => a.Label == "pass");
        if (passChoice is not null) return new RtSymbol(passChoice.Label);

        return new RtSymbol(actions[0].Label);
    }

    private static LegalAction? PickTargetForCpu(
        InterpreterRun run,
        IReadOnlyList<LegalAction> actions,
        int cpuEntityId)
    {
        LegalAction? best = null;
        int bestHp = int.MaxValue;
        foreach (var a in actions)
        {
            if (a.Kind != "target_entity") continue;
            if (a.Metadata?.TryGetValue("entityId", out var idStr) != true) continue;
            if (!int.TryParse(idStr, out var id)) continue;
            if (!run.State.Entities.TryGetValue(id, out var entity)) continue;
            if (entity.OwnerId is null) continue;
            if (entity.OwnerId == cpuEntityId) continue; // never target self
            // Prefer lowest live HP counter > 0 so damage finishes things off.
            int hp = int.MaxValue;
            foreach (var counter in new[] { "integrity", "current_ramparts", "current_hp" })
            {
                if (!entity.Counters.TryGetValue(counter, out var v)) continue;
                if (v > 0 && v < hp) hp = v;
            }
            if (hp < bestHp || best is null)
            {
                best = a;
                bestHp = hp;
            }
        }
        return best;
    }

    private static bool IsUnitPlay(InterpreterRun run, LegalAction action)
    {
        // Metadata.type (added in CPU pass) or a CardDecl lookup.
        if (action.Metadata?.TryGetValue("type", out var t) == true && t == "Unit") return true;
        if (action.Metadata?.TryGetValue("cardName", out var name) != true || name is null) return false;
        return run.State.CardDecls.TryGetValue(name, out var decl) && GetCardDeclType(decl) == "Unit";
    }

    private static string? GetCardDeclType(Ccgnf.Ast.AstCardDecl decl)
    {
        foreach (var f in decl.Body.Fields)
        {
            if (f.Key.Name != "type") continue;
            if (f.Value is Ccgnf.Ast.AstFieldExpr fe && fe.Value is Ccgnf.Ast.AstIdent id)
                return id.Name;
        }
        return null;
    }

    private static LegalAction? PickArenaForCpu(
        InterpreterRun run,
        IReadOnlyList<LegalAction> actions,
        int cpuEntityId)
    {
        var arenas = actions.Where(a => a.Kind == "target_arena").ToList();
        if (arenas.Count == 0) return null;

        // Prefer arena where opponent has at least one Unit (for Clash pairing).
        foreach (var a in arenas)
        {
            if (a.Metadata?.TryGetValue("pos", out var pos) != true || string.IsNullOrEmpty(pos)) continue;
            bool opponentHasUnit = run.State.Entities.Values.Any(e =>
                e.Kind == "Card" &&
                e.OwnerId is int oid && oid != cpuEntityId &&
                e.Characteristics.TryGetValue("in_play", out var ip) &&
                ip is RtBool rb && rb.V &&
                e.Parameters.TryGetValue("arena", out var arenaParam) &&
                arenaParam is RtSymbol ap && ap.Name == pos);
            if (opponentHasUnit) return a;
        }

        // Otherwise: first arena whose opponent conduit is still standing.
        foreach (var a in arenas)
        {
            if (a.Metadata?.TryGetValue("pos", out var pos) != true || string.IsNullOrEmpty(pos)) continue;
            bool conduitStanding = run.State.Entities.Values.Any(e =>
                e.Kind == "Conduit" &&
                e.OwnerId is int oid && oid != cpuEntityId &&
                !e.Tags.Contains("collapsed") &&
                e.Parameters.TryGetValue("arena", out var arenaParam) &&
                arenaParam is RtSymbol ap && ap.Name == pos);
            if (conduitStanding) return a;
        }
        return arenas[0];
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
