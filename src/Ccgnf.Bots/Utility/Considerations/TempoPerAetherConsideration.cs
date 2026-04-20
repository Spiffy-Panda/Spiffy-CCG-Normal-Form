using Ccgnf.Interpreter;

namespace Ccgnf.Bots.Utility.Considerations;

/// <summary>
/// Force-per-aether: "how much body do I get for my mana?" Computed as
/// <c>force / max(cost, 1)</c>, normalised against an upper bound of 4
/// (a 4-force-for-1-aether card is effectively maximally efficient).
/// <para>
/// Only applies to <c>play_card</c> actions. A card with no <c>force</c>
/// in metadata (Maneuvers without a body) scores 0.
/// </para>
/// </summary>
public sealed class TempoPerAetherConsideration : IConsideration
{
    public string Key => "tempo_per_aether";

    private const float UpperBound = 4f;

    private static readonly ResponseCurve _curve = new(new[]
    {
        new Keyframe(0.0f, 0.0f),
        new Keyframe(0.25f, 0.4f),
        new Keyframe(0.5f, 0.75f),
        new Keyframe(1.0f, 1.0f),
    });

    public bool Handles(string actionKind) => actionKind == "play_card";

    public float Score(ScoringContext ctx, LegalAction action)
    {
        if (!OnCurveConsideration.TryGetCost(action, out var cost)) return 0f;
        if (!TryGetForce(action, out var force)) return 0f;
        float ratio = force / (float)Math.Max(cost, 1);
        float x = Math.Min(ratio, UpperBound) / UpperBound;
        return CurveEvaluator.Evaluate(_curve, x);
    }

    private static bool TryGetForce(LegalAction action, out int force)
    {
        force = 0;
        if (action.Metadata is null) return false;
        if (!action.Metadata.TryGetValue("force", out var s)) return false;
        return int.TryParse(s, out force);
    }
}
