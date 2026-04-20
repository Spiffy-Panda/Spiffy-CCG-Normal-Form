using Ccgnf.Interpreter;

namespace Ccgnf.Bots.Utility.Considerations;

/// <summary>
/// "Am I playing on-curve?" Highest score when the card's cost exactly
/// matches the CPU's current aether. Decays linearly as the |diff|
/// grows: same cost → 1.0, ±1 → 0.5, ±2 or more → 0.0.
/// <para>
/// Only applies to <c>play_card</c> actions. If the action's metadata
/// lacks a parseable <c>cost</c>, score is 0.
/// </para>
/// </summary>
public sealed class OnCurveConsideration : IConsideration
{
    public string Key => "on_curve";

    // Curve over |cost - aether|: maxed at 0, half at 1, zero at 2+.
    // Zero tangents hit the linear fast path in CurveEvaluator.
    private static readonly ResponseCurve _curve = new(new[]
    {
        new Keyframe(0.0f, 1.0f),
        new Keyframe(0.5f, 0.5f),  // domain 0..1 via 2× normalisation (|diff| of 1 → 0.5)
        new Keyframe(1.0f, 0.0f),
    });

    public bool Handles(string actionKind) => actionKind == "play_card";

    public float Score(ScoringContext ctx, LegalAction action)
    {
        if (!TryGetCost(action, out var cost)) return 0f;
        int diff = Math.Abs(cost - ctx.Cpu.Aether);
        // Normalise |diff| over [0, 2] → [0, 1]; any diff ≥ 2 clamps to 1.
        float x = Math.Min(diff, 2) / 2f;
        return CurveEvaluator.Evaluate(_curve, x);
    }

    internal static bool TryGetCost(LegalAction action, out int cost)
    {
        cost = 0;
        if (action.Metadata is null) return false;
        if (!action.Metadata.TryGetValue("cost", out var s)) return false;
        return int.TryParse(s, out cost);
    }
}
