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

    private static GameState RunEncoding(int seed, IEnumerable<RtValue>? inputs = null)
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
        });
    }

    /// <summary>
    /// Setup's MulliganPhase asks each player twice, per-player, in turn
    /// order from the first player. Feeding four "pass" symbols drives the
    /// Choice builtin through its no-op path.
    /// </summary>
    private static IEnumerable<RtValue> DefaultMulliganPasses() =>
        Enumerable.Repeat<RtValue>(new RtSymbol("pass"), 4);

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
    // Setup — structural invariants
    // -----------------------------------------------------------------------

    [Fact]
    public void Setup_CreatesTwoPlayersThreeArenasSixConduits()
    {
        var state = RunEncoding(seed: 42);

        Assert.Equal(2, state.Players.Count);
        Assert.Equal(3, state.Arenas.Count);

        int conduits = state.Entities.Values.Count(e => e.Kind == "Conduit");
        Assert.Equal(6, conduits);
    }

    [Fact]
    public void Setup_EachConduitHasIntegritySeven()
    {
        var state = RunEncoding(seed: 42);

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
        var state = RunEncoding(seed: 42);

        Assert.True(state.Game.Characteristics.TryGetValue("first_player", out var fp));
        Assert.IsType<RtEntityRef>(fp);
    }

    // -----------------------------------------------------------------------
    // Round-1 Rise — aether refresh and draw
    // -----------------------------------------------------------------------

    [Fact]
    public void Round1Rise_FirstPlayerHasThreeAether()
    {
        var state = RunEncoding(seed: 42);

        var firstPlayerRef = (RtEntityRef)state.Game.Characteristics["first_player"];
        var firstPlayer = state.Entities[firstPlayerRef.Id];

        Assert.True(firstPlayer.Counters.TryGetValue("aether", out var aether));
        Assert.Equal(3, aether);
    }

    [Fact]
    public void Round1Rise_FirstPlayerDrewOneCardOnRise()
    {
        var state = RunEncoding(seed: 42);

        var firstPlayerRef = (RtEntityRef)state.Game.Characteristics["first_player"];
        var firstPlayer = state.Entities[firstPlayerRef.Id];

        // First player drew 5 at Setup (InitialDraws) + 1 at Rise = 6.
        Assert.Equal(6, firstPlayer.Zones["Hand"].Count);
    }

    [Fact]
    public void Round1Rise_SecondPlayerHasSixCardsNoRiseDrawYet()
    {
        var state = RunEncoding(seed: 42);

        var firstPlayer = (RtEntityRef)state.Game.Characteristics["first_player"];
        var secondPlayer = state.Players.First(p => p.Id != firstPlayer.Id);

        // Second player drew 6 at Setup; Rise hasn't fired for them yet in v1.
        Assert.Equal(6, secondPlayer.Zones["Hand"].Count);
    }

    // -----------------------------------------------------------------------
    // Determinism
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
        // Not strictly guaranteed by the interface, but two different seeds
        // chosen specifically should produce two different serializations
        // (RandomChoose binds first_player differently). This guards against
        // the RNG wiring silently becoming a no-op.
        var ids = new HashSet<int>();
        for (int seed = 0; seed < 16; seed++)
        {
            var state = RunEncoding(seed);
            var fp = (RtEntityRef)state.Game.Characteristics["first_player"];
            ids.Add(fp.Id);
        }
        Assert.True(ids.Count >= 2,
            "Expected RandomChoose to select both players across seeds 0..15");
    }
}
