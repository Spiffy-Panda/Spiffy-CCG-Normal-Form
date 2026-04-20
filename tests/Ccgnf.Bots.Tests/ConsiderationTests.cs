namespace Ccgnf.Bots.Tests;

public class ConsiderationTests
{
    private static LegalAction Play(string label, int cost, int? force = null, string? type = null)
    {
        var meta = new Dictionary<string, string> { ["cost"] = cost.ToString() };
        if (force is int f) meta["force"] = f.ToString();
        if (type is not null) meta["type"] = type;
        return new LegalAction("play_card", label, meta);
    }

    // Minimal stand-in ScoringContext: ScoringContext reads from GameState,
    // which is fiddly to stand up. The considerations under test here only
    // need Cpu.Aether, so we build the bare minimum state.
    private static ScoringContext BuildContext(int aether)
    {
        var state = new GameState();
        var player = state.AllocateEntity("Player", "Player1");
        state.Players.Add(player);
        player.Counters["aether"] = aether;
        var pending = new InputRequest("prompt", player.Id, Array.Empty<LegalAction>());
        return new ScoringContext(state, pending, player.Id, Intent.Default);
    }

    [Theory]
    [InlineData(2, 2, 1.0f)]   // on-curve: cost == aether
    [InlineData(1, 2, 0.5f)]   // ±1 off: half
    [InlineData(3, 2, 0.5f)]   // ±1 off: half
    [InlineData(0, 2, 0.0f)]   // ±2 off: zero
    [InlineData(4, 2, 0.0f)]   // ±2 off: zero
    [InlineData(5, 2, 0.0f)]   // ±3 off: clamps to zero
    public void OnCurveScoresByAbsDifference(int cost, int aether, float expected)
    {
        var cons = new OnCurveConsideration();
        var ctx = BuildContext(aether);
        var score = cons.Score(ctx, Play("spark", cost));
        Assert.Equal(expected, score, precision: 4);
    }

    [Fact]
    public void OnCurveIgnoresActionsWithoutCost()
    {
        var cons = new OnCurveConsideration();
        var ctx = BuildContext(2);
        var action = new LegalAction("play_card", "spark", Metadata: null);
        Assert.Equal(0f, cons.Score(ctx, action));
    }

    [Fact]
    public void OnCurveOnlyHandlesPlayCard()
    {
        var cons = new OnCurveConsideration();
        Assert.True(cons.Handles("play_card"));
        Assert.False(cons.Handles("target_entity"));
        Assert.False(cons.Handles("declare_attacker"));
        Assert.False(cons.Handles("pass_priority"));
    }

    [Theory]
    [InlineData(1, 4, 1.0f)]     // 4/1 = 4 → max
    [InlineData(2, 4, 0.75f)]    // 4/2 = 2 → halfway
    [InlineData(4, 4, 0.4f)]     // 4/4 = 1 → quarter of domain
    [InlineData(1, 0, 0.0f)]     // 0 force → 0
    public void TempoPerAetherRatiosNormalisedAcrossUpperBound(
        int cost, int force, float expected)
    {
        var cons = new TempoPerAetherConsideration();
        var ctx = BuildContext(aether: 3);
        var score = cons.Score(ctx, Play("body", cost, force));
        Assert.Equal(expected, score, precision: 4);
    }

    [Fact]
    public void TempoPerAetherMissingForceScoresZero()
    {
        var cons = new TempoPerAetherConsideration();
        var ctx = BuildContext(aether: 3);
        Assert.Equal(0f, cons.Score(ctx, Play("maneuver", cost: 2)));
    }
}
