using Ccgnf.Interpreter;

namespace Ccgnf.Bots.Utility.Considerations;

/// <summary>
/// Arena picking: prefer arenas that force Unit-vs-Unit interaction.
/// <list type="bullet">
///   <item>Opponent has a Unit there → 1.0 (Clash incoming).</item>
///   <item>Only we have a Unit there → 0.3 (safe push, but not priority).</item>
///   <item>Neither side has a Unit → 0.6 (contested, decent play).</item>
/// </list>
/// </summary>
public sealed class OverlapConsideration : IConsideration
{
    public string Key => "overlap";

    public bool Handles(string actionKind) => actionKind == "target_arena";

    public float Score(ScoringContext ctx, LegalAction action)
    {
        if (!TryGetPos(action, out var pos)) return 0f;

        int oppUnits = ctx.Opponent.UnitsByArena.GetValueOrDefault(pos);
        int ourUnits = ctx.Cpu.UnitsByArena.GetValueOrDefault(pos);

        if (oppUnits > 0) return 1.0f;
        if (ourUnits > 0) return 0.3f;
        return 0.6f;
    }

    internal static bool TryGetPos(LegalAction action, out string pos)
    {
        pos = string.Empty;
        if (action.Metadata is null) return false;
        if (!action.Metadata.TryGetValue("pos", out var s)) return false;
        if (string.IsNullOrEmpty(s)) return false;
        pos = s;
        return true;
    }
}
