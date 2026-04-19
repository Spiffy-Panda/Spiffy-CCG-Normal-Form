using Ccgnf.Interpreter;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using InterpreterRt = Ccgnf.Interpreter.Interpreter;

namespace Ccgnf.Tests;

public class InterpreterTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static GameState RunEncoding(
        int seed,
        IEnumerable<RtValue>? inputs = null,
        Func<GameEvent, GameState, bool>? shouldHalt = null)
    {
        var repoRoot = FindRepoRoot();
        var encDir = Path.Combine(repoRoot, "encoding");
        var loader = new ProjectLoader(NullLoggerFactory.Instance);
        var loadResult = loader.LoadFromDirectory(encDir);
        Assert.True(loadResult.File is not null,
            "Project failed to load:\n" + string.Join("\n", loadResult.Diagnostics));
        Assert.False(loadResult.HasErrors,
            "Project has diagnostics:\n" + string.Join("\n", loadResult.Diagnostics));

        var interpreter = new InterpreterRt(NullLogger<InterpreterRt>.Instance, NullLoggerFactory.Instance);
        return interpreter.Run(loadResult.File!, new InterpreterOptions
        {
            Seed = seed,
            Inputs = new QueuedInputs(inputs ?? DefaultMulliganPasses()),
            ShouldHalt = shouldHalt,
        });
    }

    /// <summary>
    /// Setup's MulliganPhase now fires for real (step 8a fixed the
    /// <c>Game.max_mulligans</c> reference). Two players × two repeats =
    /// four pass symbols drain Mulligan's <c>Choice</c> builtin through its
    /// NoOp branch.
    /// </summary>
    private static IEnumerable<RtValue> DefaultMulliganPasses() =>
        Enumerable.Repeat<RtValue>(new RtSymbol("pass"), 4);

    /// <summary>
    /// Halt the event loop the first time <c>Event.PhaseBegin(phase=X, ...)</c>
    /// dispatches. Since each phase handler ends with
    /// <c>BeginPhase(next, player: p)</c>, halting at a phase marker freezes
    /// the state right after the previous phase's effects have been applied.
    /// </summary>
    private static Func<GameEvent, GameState, bool> HaltAtPhase(string phase) =>
        (ev, _) => ev.TypeName == "PhaseBegin"
            && ev.Fields.TryGetValue("phase", out var p)
            && p is RtSymbol s && s.Name == phase;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ccgnf.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repo root.");
    }

    // -----------------------------------------------------------------------
    // Setup — structural invariants (halt before Rise starts looping).
    // -----------------------------------------------------------------------

    [Fact]
    public void Setup_CreatesTwoPlayersThreeArenasSixConduits()
    {
        // Halt at the very first Rise — Setup's effects are complete at the
        // point its sequence emitted PhaseBegin(Rise), but no Rise body has
        // run yet (ShouldHalt fires before the event is dispatched isn't
        // true here; it fires *after* dispatch, so Rise's RefillAether etc.
        // have applied. For pure-setup invariants that's still fine).
        var state = RunEncoding(seed: 42, shouldHalt: HaltAtPhase("Rise"));

        Assert.Equal(2, state.Players.Count);
        Assert.Equal(3, state.Arenas.Count);

        int conduits = state.Entities.Values.Count(e => e.Kind == "Conduit");
        Assert.Equal(6, conduits);
    }

    [Fact]
    public void Setup_EachConduitHasIntegritySeven()
    {
        var state = RunEncoding(seed: 42, shouldHalt: HaltAtPhase("Rise"));

        var conduits = state.Entities.Values.Where(e => e.Kind == "Conduit").ToList();
        foreach (var c in conduits)
        {
            Assert.True(c.Counters.TryGetValue("integrity", out var hp));
            Assert.Equal(7, hp);
        }
    }

    [Fact]
    public void Setup_AssignsAFirstPlayer()
    {
        var state = RunEncoding(seed: 42, shouldHalt: HaltAtPhase("Rise"));

        Assert.True(state.Game.Characteristics.TryGetValue("first_player", out var fp));
        Assert.IsType<RtEntityRef>(fp);
    }

    // -----------------------------------------------------------------------
    // Round-1 Rise — aether refresh and draw. Halt at the first
    // PhaseBegin(Channel, ...) so Round-1 Rise has exactly fired once for
    // the first player and nothing after.
    // -----------------------------------------------------------------------

    [Fact]
    public void Round1Rise_FirstPlayerHasThreeAether()
    {
        var state = RunEncoding(seed: 42, shouldHalt: HaltAtPhase("Channel"));

        var firstPlayerRef = (RtEntityRef)state.Game.Characteristics["first_player"];
        var firstPlayer = state.Entities[firstPlayerRef.Id];

        Assert.True(firstPlayer.Counters.TryGetValue("aether", out var aether));
        Assert.Equal(3, aether);
    }

    [Fact]
    public void Round1Rise_FirstPlayerDrewOneCardOnRise()
    {
        var state = RunEncoding(seed: 42, shouldHalt: HaltAtPhase("Channel"));

        var firstPlayerRef = (RtEntityRef)state.Game.Characteristics["first_player"];
        var firstPlayer = state.Entities[firstPlayerRef.Id];

        // First player drew 5 at Setup (InitialDraws) + 1 at Rise = 6.
        Assert.Equal(6, firstPlayer.Zones["Hand"].Count);
    }

    [Fact]
    public void Round1Rise_SecondPlayerHasSixCardsNoRiseDrawYet()
    {
        var state = RunEncoding(seed: 42, shouldHalt: HaltAtPhase("Channel"));

        var firstPlayer = (RtEntityRef)state.Game.Characteristics["first_player"];
        var secondPlayer = state.Players.First(p => p.Id != firstPlayer.Id);

        // Second player drew 6 at Setup; Rise hasn't fired for them yet.
        Assert.Equal(6, secondPlayer.Zones["Hand"].Count);
    }

    // -----------------------------------------------------------------------
    // Turn rotation (new in 8a)
    // -----------------------------------------------------------------------

    [Fact]
    public void TurnRotation_WalksAllFivePhasesInOrder()
    {
        var phases = new List<string>();
        var interpreter = new InterpreterRt(NullLogger<InterpreterRt>.Instance, NullLoggerFactory.Instance);
        var repoRoot = FindRepoRoot();
        var load = new ProjectLoader(NullLoggerFactory.Instance)
            .LoadFromDirectory(Path.Combine(repoRoot, "encoding"));

        // Halt the second time second-player's Pass fires — by then we've
        // seen a full two-player round cycle plus a few more phases.
        int passesSeen = 0;
        var state = interpreter.Run(load.File!, new InterpreterOptions
        {
            Seed = 42,
            Inputs = new QueuedInputs(DefaultMulliganPasses()),
            OnEvent = (ev, _) =>
            {
                if (ev.TypeName != "PhaseBegin") return;
                if (!ev.Fields.TryGetValue("phase", out var v) || v is not RtSymbol sym) return;
                phases.Add(sym.Name);
                if (sym.Name == "Pass") passesSeen++;
            },
            ShouldHalt = (_, _) => passesSeen >= 2,
        });

        // Exactly one full round: Rise Channel Clash Fall Pass twice.
        Assert.Equal(
            new[] { "Rise", "Channel", "Clash", "Fall", "Pass",
                    "Rise", "Channel", "Clash", "Fall", "Pass" },
            phases);
    }

    [Fact]
    public void TurnRotation_RoundCounterIncrementsWhenControlReturnsToFirstPlayer()
    {
        // Halt at Rise of round 2 — that's the Rise that fires right after
        // second-player's Pass has incremented the round.
        var interpreter = new InterpreterRt(NullLogger<InterpreterRt>.Instance, NullLoggerFactory.Instance);
        var repoRoot = FindRepoRoot();
        var load = new ProjectLoader(NullLoggerFactory.Instance)
            .LoadFromDirectory(Path.Combine(repoRoot, "encoding"));

        var state = interpreter.Run(load.File!, new InterpreterOptions
        {
            Seed = 42,
            Inputs = new QueuedInputs(DefaultMulliganPasses()),
            ShouldHalt = (ev, st) =>
                ev.TypeName == "PhaseBegin"
                && ev.Fields.TryGetValue("phase", out var p)
                && p is RtSymbol s && s.Name == "Rise"
                && st.Game.Counters.GetValueOrDefault("round", 0) == 2,
        });

        Assert.Equal(2, state.Game.Counters["round"]);
    }

    [Fact]
    public void DeckOut_EmitsLoseAndTerminates()
    {
        // Let the game run freely with both seats just passing mulligan —
        // each Rise draws one card; 25 rounds in, the player who drew
        // first (5 cards) runs out. Lose fires; the interpreter flips
        // GameOver and halts.
        var state = RunEncoding(seed: 42);
        Assert.True(state.GameOver);
        Assert.True(state.StepCount > 0);
    }

    // -----------------------------------------------------------------------
    // Mulligan (new in 8a) — the Choice branch actually runs.
    // -----------------------------------------------------------------------

    [Fact]
    public void Mulligan_SubmitMulligan_SetsFlagOnPlayer()
    {
        // First player's first mulligan prompt — pick mulligan; the other
        // three prompts pass. The stub PerformMulligan flags the player as
        // `mulliganed: true`.
        var inputs = new RtValue[]
        {
            new RtSymbol("mulligan"),
            new RtSymbol("pass"),
            new RtSymbol("pass"),
            new RtSymbol("pass"),
        };
        var state = RunEncoding(seed: 42, inputs: inputs, shouldHalt: HaltAtPhase("Rise"));

        var firstPlayerRef = (RtEntityRef)state.Game.Characteristics["first_player"];
        var firstPlayer = state.Entities[firstPlayerRef.Id];
        Assert.True(firstPlayer.Characteristics.TryGetValue("mulliganed", out var flag));
        Assert.IsType<RtBool>(flag);
        Assert.True(((RtBool)flag).V);
    }

    [Fact]
    public void Mulligan_AllPasses_LeavesNoMulliganFlag()
    {
        var state = RunEncoding(seed: 42, shouldHalt: HaltAtPhase("Rise"));

        foreach (var player in state.Players)
        {
            Assert.False(player.Characteristics.ContainsKey("mulliganed"));
        }
    }

    // -----------------------------------------------------------------------
    // Determinism — unchanged by 8a.
    // -----------------------------------------------------------------------

    [Fact]
    public void SameSeedSameInputs_ProducesIdenticalState()
    {
        var inputsA = DefaultMulliganPasses().ToList();
        var inputsB = DefaultMulliganPasses().ToList();

        var stateA = RunEncoding(seed: 1776, inputs: inputsA);
        var stateB = RunEncoding(seed: 1776, inputs: inputsB);

        Assert.Equal(
            StateSerializer.Serialize(stateA),
            StateSerializer.Serialize(stateB));
    }

    [Fact]
    public void DifferentSeed_MayProduceDifferentFirstPlayer()
    {
        var ids = new HashSet<int>();
        for (int seed = 0; seed < 16; seed++)
        {
            var state = RunEncoding(seed, shouldHalt: HaltAtPhase("Rise"));
            var fp = (RtEntityRef)state.Game.Characteristics["first_player"];
            ids.Add(fp.Id);
        }
        Assert.True(ids.Count >= 2,
            "Expected RandomChoose to select both players across seeds 0..15");
    }
}
