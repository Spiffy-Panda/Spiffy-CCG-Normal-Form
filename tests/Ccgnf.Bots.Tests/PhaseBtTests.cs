using Ccgnf.Bots.Bt;

namespace Ccgnf.Bots.Tests;

public class PhaseBtTests
{
    // ─── BtRunner + condition DSL ──────────────────────────────────────

    private sealed class StubBtCtx : IBtContext
    {
        public Dictionary<string, float> Vars { get; } = new();
        public string? LastAction { get; private set; }
        public float ResolveVariable(string name) => Vars.GetValueOrDefault(name, 0f);
        public BtStatus ExecuteAction(string action)
        {
            LastAction = action;
            return BtStatus.Success;
        }
    }

    [Theory]
    [InlineData("always", true)]
    [InlineData("never", false)]
    [InlineData("turn_number <= 3", true)]
    [InlineData("turn_number <= 1", false)]
    [InlineData("turn_number > 1", true)]
    [InlineData("turn_number == 2", true)]
    [InlineData("turn_number != 3", true)]
    [InlineData("banner_matches >= 2", true)]
    [InlineData("banner_matches < 2", false)]
    public void EvalConditionRunsComparisons(string expr, bool expected)
    {
        var ctx = new StubBtCtx();
        ctx.Vars["turn_number"] = 2;
        ctx.Vars["banner_matches"] = 2;
        Assert.Equal(expected, BtRunner.EvalCondition(expr, ctx));
    }

    [Fact]
    public void SelectorFiresFirstMatchingGate()
    {
        var ctx = new StubBtCtx();
        ctx.Vars["foo"] = 1;

        var tree = new[]
        {
            BtNode.Sel(
                BtNode.Gate("foo == 5", BtNode.Act("skip")),
                BtNode.Gate("foo == 1", BtNode.Act("chosen")),
                BtNode.Act("fallback")),
        };
        new BtRunner(tree).Apply(ctx);
        Assert.Equal("chosen", ctx.LastAction);
    }

    [Fact]
    public void SelectorFallsThroughToLastLeaf()
    {
        var ctx = new StubBtCtx();
        var tree = new[]
        {
            BtNode.Sel(
                BtNode.Gate("never", BtNode.Act("skip1")),
                BtNode.Gate("never", BtNode.Act("skip2")),
                BtNode.Act("default")),
        };
        new BtRunner(tree).Apply(ctx);
        Assert.Equal("default", ctx.LastAction);
    }

    // ─── PhaseBtContext + default tree ─────────────────────────────────

    [Fact]
    public void DefaultPhaseBtPicksLethalCheckWhenOpponentOneConduit()
    {
        var state = BuildTwoPlayer(out var cpuId, out var oppId);
        // Only one standing opponent conduit.
        var c = state.AllocateEntity("Conduit", "last");
        c.OwnerId = oppId;
        c.Counters["integrity"] = 5;

        var selector = PhaseBtIntentSelector.Default();
        var intent = selector.Select(state, EmptyRequest(cpuId), cpuId);
        Assert.Equal(Intent.LethalCheck, intent);
    }

    [Fact]
    public void DefaultPhaseBtPicksDefendConduitWhenLowIntegrity()
    {
        var state = BuildTwoPlayer(out var cpuId, out var oppId);

        // Opponent has multiple conduits so lethal doesn't fire.
        for (int i = 0; i < 3; i++)
        {
            var oc = state.AllocateEntity("Conduit", $"opp{i}");
            oc.OwnerId = oppId;
            oc.Counters["integrity"] = 5;
        }

        // CPU has a wounded conduit.
        var mine = state.AllocateEntity("Conduit", "my_broken");
        mine.OwnerId = cpuId;
        mine.Counters["integrity"] = 2;

        state.Game!.Counters["turn_number"] = 6;  // past early tempo

        var selector = PhaseBtIntentSelector.Default();
        Assert.Equal(Intent.DefendConduit, selector.Select(state, EmptyRequest(cpuId), cpuId));
    }

    [Fact]
    public void DefaultPhaseBtPicksEarlyTempoInRoundOne()
    {
        var state = BuildTwoPlayer(out var cpuId, out var oppId);
        for (int i = 0; i < 3; i++)
        {
            var oc = state.AllocateEntity("Conduit", $"opp{i}");
            oc.OwnerId = oppId;
            oc.Counters["integrity"] = 5;
        }
        state.Game!.Counters["turn_number"] = 1;

        var selector = PhaseBtIntentSelector.Default();
        Assert.Equal(Intent.EarlyTempo, selector.Select(state, EmptyRequest(cpuId), cpuId));
    }

    [Fact]
    public void DefaultPhaseBtFallsThroughToDefault()
    {
        var state = BuildTwoPlayer(out var cpuId, out var oppId);
        // Lots of opponent conduits, CPU healthy, past round 3, no banner.
        for (int i = 0; i < 3; i++)
        {
            var oc = state.AllocateEntity("Conduit", $"opp{i}");
            oc.OwnerId = oppId;
            oc.Counters["integrity"] = 5;
        }
        state.Game!.Counters["turn_number"] = 8;

        Assert.Equal(Intent.Default,
            PhaseBtIntentSelector.Default().Select(state, EmptyRequest(cpuId), cpuId));
    }

    // ─── JSON round-trip ───────────────────────────────────────────────

    [Fact]
    public void BtSerializerRoundTripsTree()
    {
        var tree = DefaultPhaseBt.Build();
        var json = BtSerializer.Serialize(tree);
        var parsed = BtSerializer.Deserialize(json);
        Assert.Equal(tree.Count, parsed.Count);

        // Running the parsed tree against a known state should yield same intent.
        var state = BuildTwoPlayer(out var cpuId, out var _);
        state.Game!.Counters["turn_number"] = 1;
        var selA = PhaseBtIntentSelector.Default();
        var selB = new PhaseBtIntentSelector(parsed);
        Assert.Equal(
            selA.Select(state, EmptyRequest(cpuId), cpuId),
            selB.Select(state, EmptyRequest(cpuId), cpuId));
    }

    [Fact]
    public void BtSerializerRejectsMalformedJson()
    {
        Assert.Throws<BtFormatException>(() => BtSerializer.Deserialize("{not-json"));
    }

    // ─── Helpers ───────────────────────────────────────────────────────

    private static GameState BuildTwoPlayer(out int cpuId, out int oppId)
    {
        var state = new GameState();
        var game = state.AllocateEntity("Game", "Game");
        state.Game = game;
        game.Counters["turn_number"] = 1;
        var cpu = state.AllocateEntity("Player", "CPU");
        var opp = state.AllocateEntity("Player", "Human");
        state.Players.Add(cpu);
        state.Players.Add(opp);
        cpuId = cpu.Id;
        oppId = opp.Id;
        return state;
    }

    private static InputRequest EmptyRequest(int cpuId) =>
        new("prompt", cpuId, Array.Empty<LegalAction>());
}
