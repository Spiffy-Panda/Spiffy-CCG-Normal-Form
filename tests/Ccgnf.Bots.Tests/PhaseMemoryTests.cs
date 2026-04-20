using Ccgnf.Bots.Bt;

namespace Ccgnf.Bots.Tests;

public class PhaseMemoryTests
{
    [Fact]
    public void FreshMemoryHasNoBias()
    {
        var m = new PhaseMemory();
        Assert.Equal(1.0f, m.CurrentBias);
        Assert.Equal(Intent.Default, m.LastIntent);
    }

    [Fact]
    public void IntentSetGivesMaxBias()
    {
        var m = new PhaseMemory();
        m.OnDecisionRecorded(Intent.Pushing, "channel");
        Assert.Equal(Intent.Pushing, m.LastIntent);
        Assert.Equal(1.5f, m.CurrentBias, precision: 3);
    }

    [Fact]
    public void BiasDecaysEachDecision()
    {
        var m = new PhaseMemory();
        m.OnDecisionRecorded(Intent.Pushing, "channel");          // set
        m.OnDecisionRecorded(Intent.Pushing, "channel");          // age=1, bias=1.4
        Assert.Equal(1.4f, m.CurrentBias, precision: 3);
        m.OnDecisionRecorded(Intent.Pushing, "channel");          // age=2, bias=1.3
        Assert.Equal(1.3f, m.CurrentBias, precision: 3);
    }

    [Fact]
    public void BiasClampsToOneAfterMaxAge()
    {
        var m = new PhaseMemory();
        m.OnDecisionRecorded(Intent.Pushing, "channel");
        for (int i = 0; i < 10; i++) m.OnDecisionRecorded(Intent.Pushing, "channel");
        Assert.Equal(1.0f, m.CurrentBias);
    }

    [Fact]
    public void PhaseChangeResetsMemory()
    {
        var m = new PhaseMemory();
        m.OnDecisionRecorded(Intent.Pushing, "channel");
        m.OnDecisionRecorded(Intent.Pushing, "channel");
        m.OnDecisionRecorded(Intent.Pushing, "channel");
        Assert.Equal(1.3f, m.CurrentBias, precision: 3);

        m.OnDecisionRecorded(Intent.EarlyTempo, "clash");
        Assert.Equal(Intent.EarlyTempo, m.LastIntent);
        Assert.Equal(1.5f, m.CurrentBias, precision: 3);
    }

    [Fact]
    public void ChangingIntentResetsAge()
    {
        var m = new PhaseMemory();
        m.OnDecisionRecorded(Intent.Pushing, "channel");
        m.OnDecisionRecorded(Intent.Pushing, "channel");
        m.OnDecisionRecorded(Intent.EarlyTempo, "channel");
        Assert.Equal(Intent.EarlyTempo, m.LastIntent);
        Assert.Equal(1.5f, m.CurrentBias, precision: 3);
    }

    // ─── Integration with PhaseBtIntentSelector ────────────────────────

    private static GameState TwoPlayer(out int cpu, out int opp, int turn = 5)
    {
        var s = new GameState();
        var game = s.AllocateEntity("Game", "Game");
        s.Game = game;
        game.Counters["turn_number"] = turn;
        var c = s.AllocateEntity("Player", "C");
        var o = s.AllocateEntity("Player", "O");
        s.Players.Add(c);
        s.Players.Add(o);
        cpu = c.Id;
        opp = o.Id;
        for (int i = 0; i < 3; i++)
        {
            var oc = s.AllocateEntity("Conduit", $"opp{i}");
            oc.OwnerId = opp;
            oc.Counters["integrity"] = 5;
        }
        return s;
    }

    [Fact]
    public void StickyKeepsLastIntentWhenBtWouldFallThrough()
    {
        // Turn 5 + no banner → BT fresh-picks Default. Memory says Pushing.
        var state = TwoPlayer(out var cpuId, out _);
        var req = new InputRequest("p", cpuId, Array.Empty<LegalAction>());

        var memory = new PhaseMemory();
        memory.OnDecisionRecorded(Intent.Pushing, "channel"); // bias=1.5

        var sel = PhaseBtIntentSelector.Default();
        var intent = sel.SelectWithMemory(state, req, cpuId, memory);
        Assert.Equal(Intent.Pushing, intent);
    }

    [Fact]
    public void SafetyIntentsOverrideStickiness()
    {
        // Wounded friendly conduit → defend_conduit must win even if
        // memory says to keep "pushing".
        var state = TwoPlayer(out var cpuId, out _);
        var hurt = state.AllocateEntity("Conduit", "friendly");
        hurt.OwnerId = cpuId;
        hurt.Counters["integrity"] = 2;

        var req = new InputRequest("p", cpuId, Array.Empty<LegalAction>());
        var memory = new PhaseMemory();
        memory.OnDecisionRecorded(Intent.Pushing, "channel");

        var sel = PhaseBtIntentSelector.Default();
        Assert.Equal(Intent.DefendConduit, sel.SelectWithMemory(state, req, cpuId, memory));
    }

    [Fact]
    public void ExpiredStickyAllowsFreshBtDecision()
    {
        var state = TwoPlayer(out var cpuId, out _);
        var req = new InputRequest("p", cpuId, Array.Empty<LegalAction>());

        var memory = new PhaseMemory();
        memory.OnDecisionRecorded(Intent.Pushing, "channel");
        // Age out stickiness fully.
        for (int i = 0; i < memory.StickyMaxAge + 1; i++)
            memory.OnDecisionRecorded(Intent.Pushing, "channel");

        var sel = PhaseBtIntentSelector.Default();
        // With stickiness expired, the BT fresh-pick (Default at turn 5) wins.
        Assert.Equal(Intent.Default, sel.SelectWithMemory(state, req, cpuId, memory));
    }
}
