namespace Ccgnf.Bots.Tests;

/// <summary>
/// Proves the <see cref="UtilityBot"/> orchestration contract:
/// <list type="bullet">
///   <item>Scores actions via its considerations + weight table.</item>
///   <item>Falls through to the supplied <see cref="IRoomBot"/> when no
///         consideration has an opinion (score ≤ 0).</item>
///   <item>Tie-breaks deterministically (same inputs → same pick).</item>
///   <item>Surfaces per-consideration breakdowns for the debug overlay.</item>
/// </list>
/// </summary>
public class UtilityBotTests
{
    private static GameState MakeState(int cpuAether)
    {
        var state = new GameState();
        var cpu = state.AllocateEntity("Player", "CPU");
        var opp = state.AllocateEntity("Player", "Human");
        state.Players.Add(cpu);
        state.Players.Add(opp);
        cpu.Counters["aether"] = cpuAether;
        return state;
    }

    private static LegalAction Play(string label, int cost, int? force = null)
    {
        var meta = new Dictionary<string, string> { ["cost"] = cost.ToString() };
        if (force is int f) meta["force"] = f.ToString();
        return new LegalAction("play_card", label, meta);
    }

    [Fact]
    public void PicksOnCurvePlayOverOffCurve()
    {
        var bot = new UtilityBot(new IConsideration[] { new OnCurveConsideration() });
        var state = MakeState(cpuAether: 2);
        var pending = new InputRequest("Choose a card", 1, new[]
        {
            Play("play:big", cost: 5),       // way off curve → 0
            Play("play:onCurve", cost: 2),   // perfect → 1.0
            Play("play:small", cost: 0),     // ±2 off → 0
        });

        var result = bot.Choose(state, pending, cpuEntityId: 1);
        Assert.Equal("play:onCurve", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void TempoPicksHigherForcePerAether()
    {
        var bot = new UtilityBot(new IConsideration[] { new TempoPerAetherConsideration() });
        var state = MakeState(cpuAether: 3);
        var pending = new InputRequest("Choose a card", 1, new[]
        {
            Play("play:thin", cost: 2, force: 1),   // 0.5 ratio → low
            Play("play:efficient", cost: 2, force: 4), // 2 ratio → high
        });

        var result = bot.Choose(state, pending, cpuEntityId: 1);
        Assert.Equal("play:efficient", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void WeightsCompound()
    {
        // OnCurve favors the 2-cost card; TempoPerAether favors the high-force card.
        // With equal weights, the best play is the one that's both on-curve AND efficient.
        var weights = WeightTable.Uniform(new[] { "on_curve", "tempo_per_aether" });
        var bot = new UtilityBot(
            new IConsideration[] { new OnCurveConsideration(), new TempoPerAetherConsideration() },
            weights);

        var state = MakeState(cpuAether: 2);
        var pending = new InputRequest("Choose a card", 1, new[]
        {
            Play("play:curveButWeak", cost: 2, force: 1),
            Play("play:offCurveStrong", cost: 4, force: 4),
            Play("play:curveAndStrong", cost: 2, force: 4),
        });

        var result = bot.Choose(state, pending, cpuEntityId: 1);
        Assert.Equal("play:curveAndStrong", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void IntentShiftsWinner()
    {
        // Early tempo: on-curve dominates. Pushing: tempo dominates. Build a case
        // where the answer depends on which intent the selector returns.
        var weights = new WeightTable(new Dictionary<Intent, Dictionary<string, float>>
        {
            [Intent.EarlyTempo] = new() { ["on_curve"] = 3.0f, ["tempo_per_aether"] = 0.5f },
            [Intent.Pushing]    = new() { ["on_curve"] = 0.5f, ["tempo_per_aether"] = 3.0f },
        });

        var state = MakeState(cpuAether: 2);
        var pending = new InputRequest("Choose a card", 1, new[]
        {
            Play("play:curve",  cost: 2, force: 1),   // on-curve, weak
            Play("play:strong", cost: 4, force: 4),   // off-curve, strong
        });

        var earlyBot = new UtilityBot(
            new IConsideration[] { new OnCurveConsideration(), new TempoPerAetherConsideration() },
            weights,
            selectIntent: (_, _, _) => Intent.EarlyTempo);
        Assert.Equal("play:curve",
            Assert.IsType<RtSymbol>(earlyBot.Choose(state, pending, 1)).Name);

        var pushBot = new UtilityBot(
            new IConsideration[] { new OnCurveConsideration(), new TempoPerAetherConsideration() },
            weights,
            selectIntent: (_, _, _) => Intent.Pushing);
        Assert.Equal("play:strong",
            Assert.IsType<RtSymbol>(pushBot.Choose(state, pending, 1)).Name);
    }

    [Fact]
    public void FallsBackWhenNoConsiderationHandlesAction()
    {
        // OnCurve only handles play_card. A target_entity pending with no scored
        // actions must delegate to the injected fallback.
        var fallbackCalled = 0;
        RtValue fallbackPick = new RtSymbol("fallback_chose");
        var fallback = new DelegateBot((_, _, _) =>
        {
            fallbackCalled++;
            return fallbackPick;
        });

        var bot = new UtilityBot(
            new IConsideration[] { new OnCurveConsideration() },
            fallback: fallback);

        var state = MakeState(cpuAether: 3);
        var pending = new InputRequest("Choose a target", 1, new[]
        {
            new LegalAction("target_entity", "t:1",
                new Dictionary<string, string> { ["entityId"] = "99" }),
        });

        var result = bot.Choose(state, pending, cpuEntityId: 1);
        Assert.Equal("fallback_chose", Assert.IsType<RtSymbol>(result).Name);
        Assert.Equal(1, fallbackCalled);
    }

    [Fact]
    public void ZeroWeightSuppressesConsideration()
    {
        // Even though OnCurve would pick play:onCurve with weight > 0, a weights
        // table that maps on_curve → 0 must zero-out its contribution and fall
        // back to whatever the other consideration (if any) or ladder says.
        var weights = new WeightTable(new Dictionary<Intent, Dictionary<string, float>>
        {
            [Intent.Default] = new() { ["on_curve"] = 0f },
        });

        var fallback = new DelegateBot((_, _, _) => new RtSymbol("fallback"));
        var bot = new UtilityBot(
            new IConsideration[] { new OnCurveConsideration() },
            weights,
            fallback: fallback);

        var state = MakeState(cpuAether: 2);
        var pending = new InputRequest("Choose", 1, new[] { Play("play:a", cost: 2) });

        var result = bot.Choose(state, pending, cpuEntityId: 1);
        Assert.Equal("fallback", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void TiesAreBrokenDeterministically()
    {
        // Two identically-scored actions: the same state + pending must always
        // produce the same pick across invocations (replayability).
        var bot = new UtilityBot(new IConsideration[] { new OnCurveConsideration() });
        var state = MakeState(cpuAether: 2);
        var pending = new InputRequest("Choose a card", 1, new[]
        {
            Play("play:alpha", cost: 2),
            Play("play:bravo", cost: 2),
            Play("play:charlie", cost: 2),
        });

        var first = bot.Choose(state, pending, cpuEntityId: 1);
        for (int i = 0; i < 10; i++)
        {
            var again = bot.Choose(state, pending, cpuEntityId: 1);
            Assert.Equal(first, again);
        }
    }

    [Fact]
    public void ScoreAllReturnsBreakdownSortedDescending()
    {
        var bot = new UtilityBot(
            new IConsideration[] { new OnCurveConsideration(), new TempoPerAetherConsideration() });
        var state = MakeState(cpuAether: 2);
        var pending = new InputRequest("Choose a card", 1, new[]
        {
            Play("play:weak", cost: 4, force: 1),
            Play("play:strong", cost: 2, force: 4),
        });

        var ranked = bot.ScoreAll(state, pending, cpuEntityId: 1);
        Assert.Equal(2, ranked.Count);
        Assert.Equal("play:strong", ranked[0].Action.Label);
        Assert.True(ranked[0].Score >= ranked[1].Score);
        Assert.Contains("on_curve", ranked[0].Breakdown.Keys);
        Assert.Contains("tempo_per_aether", ranked[0].Breakdown.Keys);
    }

    [Fact]
    public void EmptyActionsReturnsPass()
    {
        var bot = new UtilityBot(new IConsideration[] { new OnCurveConsideration() });
        var state = MakeState(cpuAether: 0);
        var pending = new InputRequest("empty", 1, Array.Empty<LegalAction>());

        var result = bot.Choose(state, pending, cpuEntityId: 1);
        Assert.Equal("pass", Assert.IsType<RtSymbol>(result).Name);
    }

    private sealed class DelegateBot : IRoomBot
    {
        private readonly Func<GameState, InputRequest, int, RtValue> _fn;
        public DelegateBot(Func<GameState, InputRequest, int, RtValue> fn) { _fn = fn; }
        public RtValue Choose(GameState state, InputRequest pending, int cpuEntityId) =>
            _fn(state, pending, cpuEntityId);
    }
}
