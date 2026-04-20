using Ccgnf.Interpreter;

namespace Ccgnf.Bots.Utility.Considerations;

/// <summary>
/// One-ply look-ahead for attack declarations. For a <c>declare_attacker</c>
/// action labelled <c>attack</c>: score 1.0 unless doing so would leave a
/// friendly conduit exposed to a retaliation we can't survive. Specifically,
/// if the opponent's total unit force in an arena where we have a conduit
/// meets or exceeds our conduit's integrity, score 0 (don't attack — a
/// dead attacker loses the race).
/// <para>
/// Pass-priority alternatives ("pass" labelled declare_attacker actions)
/// score 0 so the consideration never overrides them positively — they're
/// only picked when the fallback engages.
/// </para>
/// </summary>
public sealed class ThreatAvoidanceConsideration : IConsideration
{
    public string Key => "threat_avoidance";

    public bool Handles(string actionKind) => actionKind == "declare_attacker";

    public float Score(ScoringContext ctx, LegalAction action)
    {
        if (!string.Equals(action.Label, "attack", StringComparison.Ordinal))
            return 0f;

        foreach (var (arena, conduit) in ctx.Cpu.ConduitByArena)
        {
            int integrity = conduit.Counters.TryGetValue("integrity", out var v) ? v : 0;
            if (integrity <= 0) continue;
            int oppForce = ctx.Opponent.UnitForceByArena.GetValueOrDefault(arena);
            if (oppForce >= integrity) return 0f;
        }

        return 1f;
    }
}
