using Ccgnf.Interpreter;

namespace Ccgnf.Bots.Utility.Considerations;

/// <summary>
/// Category-based priority for <c>target_entity</c> picks:
/// Conduit (1.0) &gt; Card/Unit (0.6) &gt; other owned entities (0.3).
/// Self-targeting scores 0 so it never beats "real" targets. The target
/// must be owned by someone; orphaned entities (Arenas, Game itself)
/// score 0.
/// </summary>
public sealed class OpponentPriorityConsideration : IConsideration
{
    public string Key => "opponent_priority";

    public bool Handles(string actionKind) => actionKind == "target_entity";

    public float Score(ScoringContext ctx, LegalAction action)
    {
        if (action.Metadata is null) return 0f;
        if (!action.Metadata.TryGetValue("entityId", out var idStr)) return 0f;
        if (!int.TryParse(idStr, out var id)) return 0f;
        if (!ctx.State.Entities.TryGetValue(id, out var entity)) return 0f;
        if (entity.OwnerId is null) return 0f;
        if (entity.OwnerId == ctx.CpuEntityId) return 0f;

        return entity.Kind switch
        {
            "Conduit" => 1.0f,
            "Card" => 0.6f,
            _ => 0.3f,
        };
    }
}
