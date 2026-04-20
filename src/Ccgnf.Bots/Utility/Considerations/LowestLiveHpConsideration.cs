using Ccgnf.Interpreter;

namespace Ccgnf.Bots.Utility.Considerations;

/// <summary>
/// For a <c>target_entity</c> action, score by how close the target is to
/// dying: <c>1 - minLiveHp/cap</c> where cap is a fixed upper bound (8).
/// Never target self (score 0), never target already-collapsed things
/// (score 0). Prefers finishing blows over damage spread.
/// </summary>
public sealed class LowestLiveHpConsideration : IConsideration
{
    public string Key => "lowest_live_hp";

    private const float HpCap = 8f;

    public bool Handles(string actionKind) => actionKind == "target_entity";

    public float Score(ScoringContext ctx, LegalAction action)
    {
        if (action.Metadata is null) return 0f;
        if (!action.Metadata.TryGetValue("entityId", out var idStr)) return 0f;
        if (!int.TryParse(idStr, out var id)) return 0f;
        if (!ctx.State.Entities.TryGetValue(id, out var entity)) return 0f;
        if (entity.OwnerId == ctx.CpuEntityId) return 0f;

        int minHp = int.MaxValue;
        foreach (var counter in new[] { "integrity", "current_ramparts", "current_hp" })
        {
            if (!entity.Counters.TryGetValue(counter, out var v)) continue;
            if (v > 0 && v < minHp) minHp = v;
        }
        if (minHp == int.MaxValue) return 0f;

        float normalised = Math.Min(minHp, HpCap) / HpCap;
        return 1f - normalised;
    }
}
