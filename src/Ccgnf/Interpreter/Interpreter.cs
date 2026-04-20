using Ccgnf.Ast;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ccgnf.Interpreter;

/// <summary>
/// Configuration for a single interpreter run.
/// </summary>
public sealed class InterpreterOptions
{
    /// <summary>Seed for the scheduler's <see cref="Random"/>.</summary>
    public int Seed { get; set; }

    /// <summary>Pre-sequenced host inputs; defaults to empty. Ignored by <see cref="Interpreter.StartRun"/>, which installs its own blocking channel.</summary>
    public IHostInputQueue? Inputs { get; set; }

    /// <summary>Cards to seed into each player's Arsenal before Setup.</summary>
    public int DefaultDeckSize { get; set; } = 30;

    /// <summary>Safety cap on events processed in one run (v1 guard).</summary>
    public int MaxEventDispatches { get; set; } = 10_000;

    /// <summary>
    /// Per-player initial decks. Positional — <c>InitialDecks[i]</c> applies
    /// to <c>state.Players[i]</c> (declaration order from the encoding, which
    /// matches roster order for pre-seated CPUs followed by humans). Each
    /// inner list is the card names to seed into that player's Arsenal;
    /// <c>null</c> falls back to <see cref="DefaultDeckSize"/> anonymous
    /// placeholders. Names become each <c>Card</c> entity's
    /// <c>DisplayName</c>, so hosts can resolve them back to their catalog
    /// entry via a name lookup.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>?>? InitialDecks { get; set; }

    /// <summary>
    /// Fires on the interpreter thread after each <see cref="GameEvent"/> is
    /// dispatched. Hosts use this to stream SSE frames as the run advances;
    /// the handler must not block or throw.
    /// </summary>
    public Action<GameEvent, GameState>? OnEvent { get; set; }

    /// <summary>
    /// Optional predicate evaluated <b>before</b> each event dispatch. When
    /// it returns true, the event loop exits cleanly without firing the
    /// current event — state reflects everything up to (but not including)
    /// that event. Tests use this to freeze the run at a phase boundary
    /// (e.g. halt at <c>PhaseBegin(Channel, …)</c> to capture the end of
    /// Rise without entering the Main-phase priority window that would
    /// block on host input).
    /// </summary>
    public Func<GameEvent, GameState, bool>? ShouldHalt { get; set; }
}

/// <summary>
/// Top-level runtime: assembles a <see cref="GameState"/> from a validated
/// <see cref="AstFile"/>, emits the initial <c>Event.GameStart</c>, and drives
/// the event loop until it drains. v1 scope is Setup through the first
/// player's Round-1 Rise phase (GrammarSpec §8).
///
/// Two entry shapes:
/// <list type="bullet">
///   <item><see cref="Run"/> — synchronous wrapper, drives a pre-sequenced
///     <see cref="IHostInputQueue"/> to completion. Suited to tests and the
///     stateless <c>/api/run</c> endpoint.</item>
///   <item><see cref="StartRun"/> — returns an <see cref="InterpreterRun"/>
///     handle; the event loop runs on a background task and suspends at each
///     <c>Choice</c> for a consumer to <c>Submit</c>. Suited to rooms where
///     actions arrive over time.</item>
/// </list>
/// </summary>
public sealed class Interpreter
{
    private readonly ILogger<Interpreter> _log;
    private readonly ILoggerFactory _loggerFactory;

    public Interpreter(ILogger<Interpreter>? log = null, ILoggerFactory? loggerFactory = null)
    {
        _log = log ?? NullLogger<Interpreter>.Instance;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <summary>
    /// Synchronous run. Drives the event loop to completion against the
    /// pre-sequenced <see cref="InterpreterOptions.Inputs"/>. Throws if the
    /// interpreter requests more inputs than were supplied.
    /// </summary>
    public GameState Run(AstFile file, InterpreterOptions? options = null)
    {
        options ??= new InterpreterOptions();
        var inputs = options.Inputs ?? new QueuedInputs(Array.Empty<RtValue>());

        using var handle = StartRun(file, new InterpreterOptions
        {
            Seed = options.Seed,
            DefaultDeckSize = options.DefaultDeckSize,
            MaxEventDispatches = options.MaxEventDispatches,
            OnEvent = options.OnEvent,
            ShouldHalt = options.ShouldHalt,
            InitialDecks = options.InitialDecks,
            // Inputs intentionally omitted — StartRun installs its own channel.
        });

        while (true)
        {
            var pending = handle.WaitPending();
            if (pending is null) break;
            handle.Submit(inputs.Next(pending));
        }

        // Propagate faults so callers of Run see them, not a silent bad state.
        if (handle.Status == RunStatus.Faulted && handle.Fault is not null)
        {
            throw handle.Fault;
        }

        _log.LogInformation(
            "Interpreter: run halted; {Steps} events dispatched, {Entities} entities",
            handle.State.StepCount, handle.State.Entities.Count);

        return handle.State;
    }

    /// <summary>
    /// Start an asynchronous run. Returns a handle that publishes pending
    /// inputs via <see cref="InterpreterRun.WaitPending"/> and resumes on
    /// <see cref="InterpreterRun.Submit"/>. Pre-sequenced inputs on
    /// <paramref name="options"/> are ignored — drive with <c>Submit</c>.
    /// </summary>
    public InterpreterRun StartRun(AstFile file, InterpreterOptions? options = null)
    {
        options ??= new InterpreterOptions();
        int maxDispatches = options.MaxEventDispatches;
        int seed = options.Seed;
        int deckSize = options.DefaultDeckSize;
        var onEvent = options.OnEvent;
        var shouldHalt = options.ShouldHalt;
        var initialDecks = options.InitialDecks;

        var cts = new CancellationTokenSource();
        var channel = new BlockingInputChannel(cts);

        var scheduler = new Scheduler(seed, channel, _loggerFactory.CreateLogger<Scheduler>());
        var builder = new StateBuilder(_loggerFactory.CreateLogger<StateBuilder>());
        var state = builder.Build(file, scheduler);

        SeedDecks(state, deckSize, initialDecks);

        var evaluator = new Evaluator(state, scheduler, _loggerFactory.CreateLogger<Evaluator>());

        state.PendingEvents.Enqueue(new GameEvent("GameStart",
            new Dictionary<string, RtValue>()));

        return new InterpreterRun(
            state,
            channel,
            cts,
            interpreterBody: _ =>
            {
                RunEventLoop(state, evaluator, maxDispatches, onEvent, shouldHalt);
                return Task.CompletedTask;
            });
    }

    // -------------------------------------------------------------------------
    // Deck seeding
    // -------------------------------------------------------------------------

    private static void SeedDecks(
        GameState state,
        int deckSize,
        IReadOnlyList<IReadOnlyList<string>?>? initialDecks)
    {
        for (int p = 0; p < state.Players.Count; p++)
        {
            var player = state.Players[p];
            if (!player.Zones.TryGetValue("Arsenal", out var arsenal)) continue;

            IReadOnlyList<string>? names = null;
            if (initialDecks is not null && p < initialDecks.Count) names = initialDecks[p];

            if (names is not null && names.Count > 0)
            {
                // Named cards — one entity per listed name. Arsenal size
                // matches the deck; DefaultDeckSize is ignored. Each entity
                // carries the card name as its DisplayName so hosts can
                // resolve it back to a catalog entry (for the inspector,
                // for UI labels, etc.).
                foreach (var name in names)
                {
                    var card = state.AllocateEntity("Card", name);
                    card.OwnerId = player.Id;
                    arsenal.Contents.Add(card.Id);
                }
            }
            else
            {
                // No deck supplied — fill with anonymous placeholders as v1
                // used to. Keeps tests and the stateless /api/run endpoint
                // working without forcing every caller to provide a deck.
                for (int i = 0; i < deckSize; i++)
                {
                    var card = state.AllocateEntity("Card", $"Deck_{player.DisplayName}_{i}");
                    card.OwnerId = player.Id;
                    arsenal.Contents.Add(card.Id);
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Event loop — §8.2 sketch, minus Interrupts / Replacements (not v1).
    // -------------------------------------------------------------------------

    private void RunEventLoop(
        GameState state,
        Evaluator ev,
        int maxDispatches,
        Action<GameEvent, GameState>? onEvent,
        Func<GameEvent, GameState, bool>? shouldHalt)
    {
        while (!state.GameOver && state.PendingEvents.TryDequeue(out var current))
        {
            // Pre-dispatch halt check — lets tests freeze state before a
            // phase transition that would otherwise require host input.
            if (shouldHalt is not null)
            {
                try
                {
                    if (shouldHalt(current, state))
                    {
                        // Put the event back at the front; future runs can
                        // pick up where we left off if this ever gets reused.
                        state.PendingEvents.EnqueueFront(current);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Interpreter ShouldHalt predicate threw; continuing");
                }
            }

            state.StepCount++;
            if (state.StepCount > maxDispatches)
            {
                _log.LogWarning("Interpreter: event-loop safety cap reached at {Steps} dispatches", state.StepCount);
                break;
            }

            _log.LogInformation("Dispatching {Event}", current);
            DispatchEvent(current, state, ev);
            RunSbaPass(state, ev);

            // GameEnd and Lose are both terminal — the latter covers deck-out
            // and other direct losses from §5/§7. An encoding-level trigger
            // that converts Lose → GameEnd with a winner field lands when the
            // real victory rules arrive (8g).
            if (current.TypeName == "GameEnd" || current.TypeName == "Lose")
            {
                state.GameOver = true;
            }

            if (onEvent is not null)
            {
                try { onEvent(current, state); }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Interpreter OnEvent handler threw; continuing");
                }
            }
        }
    }

    private void DispatchEvent(GameEvent current, GameState state, Evaluator ev)
    {
        // v1: no Replacement handling. Fire each matching Triggered ability on
        // every entity in declaration order — Game first (historical
        // ordering), then every other entity whose ability list is non-empty.
        // Non-Game entities bind `self` to the owner, so their patterns can
        // gate on "this event's target IS me" (the usual OnEnter / EndOfClash
        // shape).
        var game = state.Game;
        if (game is not null)
        {
            FireTriggers(game, current, ev, selfEntityId: null);
        }

        // Snapshot the entity list before iterating — a trigger might
        // instantiate a new entity (InstantiateEntity builtin), and we don't
        // want that fresh entity to fire on the event that spawned it.
        var entitySnapshot = state.Entities.Values.ToList();
        foreach (var entity in entitySnapshot)
        {
            if (entity.Kind == "Game") continue;
            if (entity.Abilities.Count == 0) continue;
            FireTriggers(entity, current, ev, selfEntityId: entity.Id);
        }
    }

    private static void FireTriggers(
        Entity owner,
        GameEvent current,
        Evaluator ev,
        int? selfEntityId)
    {
        foreach (var ability in owner.Abilities)
        {
            if (ability.Kind != AbilityKind.Triggered) continue;
            if (ability.OnPattern is null || ability.Effect is null) continue;
            if (!TryMatchPattern(ability.OnPattern, current, selfEntityId, out var bindings))
            {
                continue;
            }
            var env = RtEnv.Empty;
            if (selfEntityId is int sid)
            {
                env = env.Extend("self", new RtEntityRef(sid));
                if (owner.OwnerId is int ownerPlayerId)
                {
                    env = env.Extend("controller", new RtEntityRef(ownerPlayerId));
                }
                CastLog.RecordTrigger(owner, ability.OnPattern, current);
            }
            if (bindings.Count > 0) env = env.Extend(bindings);
            ev.Eval(ability.Effect, env);
        }
    }

    private static void RunSbaPass(GameState state, Evaluator ev)
    {
        // v1 SBA scope (8f/8g): conduit collapse + two-conduits-lost victory.
        // These rules are declared in encoding/engine/07-sba.ccgnf as Static
        // abilities with check_at: continuously; the full Static-ability
        // evaluator isn't wired yet, so we enforce them engine-side in the
        // same order the encoding expects. When the Static path lands, this
        // function becomes a driver that walks those abilities instead.
        //
        // Loops until the pass finds no new changes — damage dealt during
        // collapse processing can chain into more collapses (unlikely in v1,
        // but cheap insurance).
        bool changed = true;
        int guard = 0;
        while (changed && guard++ < 64)
        {
            changed = false;

            foreach (var entity in state.Entities.Values)
            {
                if (entity.Kind != "Conduit") continue;
                if (entity.Tags.Contains("collapsed")) continue;
                int integrity = entity.Counters.GetValueOrDefault("integrity", int.MaxValue);
                if (integrity > 0) continue;

                entity.Tags.Add("collapsed");
                entity.Characteristics["collapsed"] = new RtBool(true);

                var fields = new Dictionary<string, RtValue>
                {
                    ["conduit"] = new RtEntityRef(entity.Id),
                };
                if (entity.OwnerId is int oid) fields["owner"] = new RtEntityRef(oid);
                state.PendingEvents.Enqueue(new GameEvent("ConduitCollapsed", fields));
                changed = true;
            }

            // Two-conduits-lost victory (GameRules §7.5). If a player has
            // two or more collapsed conduits and hasn't already lost, emit
            // GameEnd with loser/winner/reason. The interpreter's main
            // event loop flips GameOver on GameEnd.
            foreach (var player in state.Players)
            {
                if (player.Tags.Contains("lost")) continue;
                int collapsed = 0;
                foreach (var entity in state.Entities.Values)
                {
                    if (entity.Kind != "Conduit") continue;
                    if (entity.OwnerId != player.Id) continue;
                    if (entity.Tags.Contains("collapsed")) collapsed++;
                }
                if (collapsed < 2) continue;

                player.Tags.Add("lost");
                var opponent = state.Players.FirstOrDefault(p => p.Id != player.Id);
                var fields = new Dictionary<string, RtValue>
                {
                    ["loser"] = new RtEntityRef(player.Id),
                    ["reason"] = new RtSymbol("TwoConduitsLost"),
                };
                if (opponent is not null) fields["winner"] = new RtEntityRef(opponent.Id);
                state.PendingEvents.Enqueue(new GameEvent("GameEnd", fields));
                changed = true;
            }
        }
        _ = ev;
    }

    // -------------------------------------------------------------------------
    // Pattern matching — Event.Foo or Event.Foo(field=value, other=bound).
    // -------------------------------------------------------------------------

    internal static bool TryMatchPattern(
        AstExpr pattern,
        GameEvent current,
        out List<(string, RtValue)> bindings) =>
        TryMatchPattern(pattern, current, selfEntityId: null, out bindings);

    internal static bool TryMatchPattern(
        AstExpr pattern,
        GameEvent current,
        int? selfEntityId,
        out List<(string, RtValue)> bindings)
    {
        bindings = new();

        if (pattern is AstMemberAccess ma &&
            ma.Target is AstIdent { Name: "Event" })
        {
            return ma.Member == current.TypeName;
        }

        if (pattern is AstFunctionCall call &&
            call.Callee is AstMemberAccess ma2 &&
            ma2.Target is AstIdent { Name: "Event" })
        {
            if (ma2.Member != current.TypeName) return false;

            foreach (var a in call.Args)
            {
                string? fieldName = null;
                AstExpr? valueExpr = null;
                switch (a)
                {
                    case AstArgNamed n: fieldName = n.Name; valueExpr = n.Value; break;
                    case AstArgBinding b: fieldName = b.Name; valueExpr = b.Value; break;
                }
                if (fieldName is null || valueExpr is null) continue;

                if (!current.Fields.TryGetValue(fieldName, out var fieldValue))
                {
                    return false;
                }

                // `self` is special: when we're dispatching a Triggered
                // ability attached to a non-Game entity, the pattern value
                // `self` means "this event must reference me". The current
                // entity's id was threaded in via selfEntityId.
                if (selfEntityId is int sid &&
                    valueExpr is AstIdent { Name: "self" })
                {
                    if (fieldValue is RtEntityRef er && er.Id == sid) continue;
                    return false;
                }

                if (IsBindingIdent(valueExpr, out var bindName))
                {
                    bindings.Add((bindName, fieldValue));
                    continue;
                }

                // Literal comparison — a capitalized symbol or other constant.
                if (valueExpr is AstIdent lit)
                {
                    if (fieldValue is RtSymbol sym && sym.Name == lit.Name) continue;
                    return false;
                }
                if (valueExpr is AstStringLit sl)
                {
                    if (fieldValue is RtString rs && rs.V == sl.Value) continue;
                    return false;
                }
                if (valueExpr is AstIntLit il)
                {
                    if (fieldValue is RtInt ri && ri.V == il.Value) continue;
                    return false;
                }
                // Any other pattern shape — bail conservatively.
                return false;
            }
            return true;
        }

        return false;
    }

    private static bool IsBindingIdent(AstExpr e, out string name)
    {
        if (e is AstIdent id && id.Name.Length > 0 && char.IsLower(id.Name[0]))
        {
            name = id.Name;
            return true;
        }
        name = "";
        return false;
    }
}
