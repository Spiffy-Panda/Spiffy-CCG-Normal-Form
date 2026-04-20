namespace Ccgnf.Bots.Bt;

/// <summary>
/// The baked-in phase selector shipped when no
/// <c>encoding/ai/phase-bt.json</c> is present. Mirrors the tree in
/// §10.2e of <c>docs/plan/steps/10.2-long-term-ai-plan.md</c>.
/// <para>Selection precedence (top to bottom):</para>
/// <list type="number">
///   <item><c>lethal_check</c> — opponent down to one conduit.</item>
///   <item><c>defend_conduit</c> — any friendly conduit at ≤ 3 integrity.</item>
///   <item><c>pushing</c> — 2+ banner-matched cards in hand.</item>
///   <item><c>early_tempo</c> — round 3 or earlier.</item>
///   <item><c>default</c> — fallback leaf (always succeeds).</item>
/// </list>
/// </summary>
public static class DefaultPhaseBt
{
    public static IReadOnlyList<BtNode> Build() => new[]
    {
        BtNode.Sel(
            BtNode.Gate("opponent_standing_conduits <= 1", BtNode.Act("intent:lethal_check")),
            BtNode.Gate("min_own_conduit_integrity <= 3",  BtNode.Act("intent:defend_conduit")),
            BtNode.Gate("banner_matches_in_hand >= 2",     BtNode.Act("intent:pushing")),
            BtNode.Gate("turn_number <= 3",                BtNode.Act("intent:early_tempo")),
            BtNode.Act("intent:default")),
    };
}
