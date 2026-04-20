using Ccgnf.Interpreter;

namespace Ccgnf.Bots.Utility.Considerations;

/// <summary>
/// Close-the-game bias: for <c>target_arena</c> picks, score inversely
/// proportional to the opponent conduit's integrity in that arena. A
/// 1-integrity conduit scores near 1.0; a full-health one scores near 0.
/// Missing conduit (already collapsed) scores 0 — the arena isn't
/// interesting for closing.
/// </summary>
public sealed class ConduitSoftnessConsideration : IConsideration
{
    public string Key => "conduit_softness";

    private const float IntegrityCap = 8f;

    public bool Handles(string actionKind) => actionKind == "target_arena";

    public float Score(ScoringContext ctx, LegalAction action)
    {
        if (!OverlapConsideration.TryGetPos(action, out var pos)) return 0f;
        if (!ctx.Opponent.ConduitByArena.TryGetValue(pos, out var conduit)) return 0f;
        if (!conduit.Counters.TryGetValue("integrity", out var integrity)) return 0f;
        if (integrity <= 0) return 0f;

        float normalised = Math.Min(integrity, IntegrityCap) / IntegrityCap;
        return 1f - normalised;
    }
}
