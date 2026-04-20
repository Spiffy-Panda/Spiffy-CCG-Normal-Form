namespace Ccgnf.Bots.Tests;

/// <summary>
/// Exercises the 10.2d considerations: target-lowest-hp, opponent-priority,
/// overlap, conduit-softness, threat-avoidance. These build on a richer
/// ScoringContext (per-arena conduit + unit lookups in PlayerView).
/// </summary>
public class TargetArenaConsiderationTests
{
    private static (GameState state, Entity cpu, Entity opp) Setup()
    {
        var state = new GameState();
        var cpu = state.AllocateEntity("Player", "CPU");
        var opp = state.AllocateEntity("Player", "Human");
        state.Players.Add(cpu);
        state.Players.Add(opp);
        return (state, cpu, opp);
    }

    private static ScoringContext BuildCtx(GameState state, int cpuId)
    {
        var pending = new InputRequest("prompt", cpuId, Array.Empty<LegalAction>());
        return new ScoringContext(state, pending, cpuId, Intent.Default);
    }

    private static LegalAction Target(int entityId) =>
        new("target_entity", $"target:{entityId}",
            new Dictionary<string, string> { ["entityId"] = entityId.ToString() });

    private static LegalAction Arena(string pos) =>
        new("target_arena", $"arena:{pos}",
            new Dictionary<string, string> { ["pos"] = pos });

    // ─── LowestLiveHp ───────────────────────────────────────────────────

    [Fact]
    public void LowestLiveHpScoresHigherForWoundedTarget()
    {
        var (state, cpu, opp) = Setup();
        var healthy = state.AllocateEntity("Conduit", "full");
        healthy.OwnerId = opp.Id;
        healthy.Counters["integrity"] = 7;
        var wounded = state.AllocateEntity("Conduit", "soft");
        wounded.OwnerId = opp.Id;
        wounded.Counters["integrity"] = 1;

        var cons = new LowestLiveHpConsideration();
        var ctx = BuildCtx(state, cpu.Id);
        var sHealthy = cons.Score(ctx, Target(healthy.Id));
        var sWounded = cons.Score(ctx, Target(wounded.Id));
        Assert.True(sWounded > sHealthy);
        Assert.Equal(0.875f, sWounded, precision: 3); // 1 - 1/8
    }

    [Fact]
    public void LowestLiveHpNeverPicksSelf()
    {
        var (state, cpu, _) = Setup();
        var friendly = state.AllocateEntity("Conduit", "mine");
        friendly.OwnerId = cpu.Id;
        friendly.Counters["integrity"] = 1;

        var cons = new LowestLiveHpConsideration();
        Assert.Equal(0f, cons.Score(BuildCtx(state, cpu.Id), Target(friendly.Id)));
    }

    // ─── OpponentPriority ───────────────────────────────────────────────

    [Fact]
    public void OpponentPriorityRanksConduitAboveCard()
    {
        var (state, cpu, opp) = Setup();
        var conduit = state.AllocateEntity("Conduit", "c");
        conduit.OwnerId = opp.Id;
        var unit = state.AllocateEntity("Card", "u");
        unit.OwnerId = opp.Id;

        var cons = new OpponentPriorityConsideration();
        var ctx = BuildCtx(state, cpu.Id);
        Assert.True(cons.Score(ctx, Target(conduit.Id)) > cons.Score(ctx, Target(unit.Id)));
    }

    [Fact]
    public void OpponentPrioritySelfScoresZero()
    {
        var (state, cpu, _) = Setup();
        var friendly = state.AllocateEntity("Card", "mine");
        friendly.OwnerId = cpu.Id;

        var cons = new OpponentPriorityConsideration();
        Assert.Equal(0f, cons.Score(BuildCtx(state, cpu.Id), Target(friendly.Id)));
    }

    // ─── Overlap ────────────────────────────────────────────────────────

    [Fact]
    public void OverlapFavoursArenaWithOpponentUnit()
    {
        var (state, cpu, opp) = Setup();
        var enemy = state.AllocateEntity("Card", "Raider");
        enemy.OwnerId = opp.Id;
        enemy.Characteristics["in_play"] = new RtBool(true);
        enemy.Parameters["arena"] = new RtSymbol("right");

        var cons = new OverlapConsideration();
        var ctx = BuildCtx(state, cpu.Id);
        Assert.Equal(1.0f, cons.Score(ctx, Arena("right")));
        Assert.Equal(0.6f, cons.Score(ctx, Arena("left")));   // empty, contested
    }

    [Fact]
    public void OverlapWhenOnlyFriendlyPresent()
    {
        var (state, cpu, _) = Setup();
        var mine = state.AllocateEntity("Card", "mine");
        mine.OwnerId = cpu.Id;
        mine.Characteristics["in_play"] = new RtBool(true);
        mine.Parameters["arena"] = new RtSymbol("middle");

        var cons = new OverlapConsideration();
        Assert.Equal(0.3f, cons.Score(BuildCtx(state, cpu.Id), Arena("middle")));
    }

    // ─── ConduitSoftness ────────────────────────────────────────────────

    [Fact]
    public void ConduitSoftnessRisesAsIntegrityFalls()
    {
        var (state, cpu, opp) = Setup();
        var hard = state.AllocateEntity("Conduit", "hard");
        hard.OwnerId = opp.Id;
        hard.Counters["integrity"] = 7;
        hard.Parameters["arena"] = new RtSymbol("left");
        var soft = state.AllocateEntity("Conduit", "soft");
        soft.OwnerId = opp.Id;
        soft.Counters["integrity"] = 1;
        soft.Parameters["arena"] = new RtSymbol("right");

        var cons = new ConduitSoftnessConsideration();
        var ctx = BuildCtx(state, cpu.Id);
        var left = cons.Score(ctx, Arena("left"));
        var right = cons.Score(ctx, Arena("right"));
        Assert.True(right > left);
    }

    [Fact]
    public void ConduitSoftnessCollapsedArenaScoresZero()
    {
        var (state, cpu, opp) = Setup();
        var gone = state.AllocateEntity("Conduit", "gone");
        gone.OwnerId = opp.Id;
        gone.Tags.Add("collapsed");
        gone.Parameters["arena"] = new RtSymbol("middle");

        var cons = new ConduitSoftnessConsideration();
        Assert.Equal(0f, cons.Score(BuildCtx(state, cpu.Id), Arena("middle")));
    }

    // ─── ThreatAvoidance ────────────────────────────────────────────────

    [Fact]
    public void ThreatAvoidanceScoresAttackWhenSafe()
    {
        var (state, cpu, _) = Setup();
        var conduit = state.AllocateEntity("Conduit", "safe");
        conduit.OwnerId = cpu.Id;
        conduit.Counters["integrity"] = 5;
        conduit.Parameters["arena"] = new RtSymbol("left");

        var cons = new ThreatAvoidanceConsideration();
        Assert.Equal(1f, cons.Score(
            BuildCtx(state, cpu.Id),
            new LegalAction("declare_attacker", "attack")));
    }

    [Fact]
    public void ThreatAvoidanceZerosAttackWhenOpponentForceMeetsConduitHp()
    {
        var (state, cpu, opp) = Setup();
        var conduit = state.AllocateEntity("Conduit", "vulnerable");
        conduit.OwnerId = cpu.Id;
        conduit.Counters["integrity"] = 2;
        conduit.Parameters["arena"] = new RtSymbol("left");

        var bruiser = state.AllocateEntity("Card", "bruiser");
        bruiser.OwnerId = opp.Id;
        bruiser.Characteristics["in_play"] = new RtBool(true);
        bruiser.Parameters["arena"] = new RtSymbol("left");
        bruiser.Counters["force"] = 3;

        var cons = new ThreatAvoidanceConsideration();
        Assert.Equal(0f, cons.Score(
            BuildCtx(state, cpu.Id),
            new LegalAction("declare_attacker", "attack")));
    }

    [Fact]
    public void ThreatAvoidanceIgnoresNonAttackLabels()
    {
        var (state, cpu, _) = Setup();
        var cons = new ThreatAvoidanceConsideration();
        Assert.Equal(0f, cons.Score(
            BuildCtx(state, cpu.Id),
            new LegalAction("declare_attacker", "pass")));
    }
}
