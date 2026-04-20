using Ccgnf.Ast;
using Ccgnf.Interpreter;

namespace Ccgnf.Bots;

/// <summary>
/// Baseline-plus CPU policy. Good enough to exercise Unit-vs-Unit clashes
/// without a real search / utility system (that's the <c>UtilityBot</c>
/// work in step 10.2). Lifted verbatim from <c>Room.ChooseCpuAction</c>
/// at commit 61afdb3 so the extraction preserves behaviour byte-for-byte.
///
/// <para>The ladder, in order:</para>
/// <list type="number">
///   <item>If a Clash attack choice is offered, always attack.</item>
///   <item>If a target-entity choice includes an opponent-owned entity,
///         pick the one with the lowest HP counter (integrity /
///         current_ramparts / current_hp). Avoids wasting damage on
///         already-collapsed conduits and ignores friendly targets that
///         would be self-damage.</item>
///   <item>If a play-card choice exists, prefer Unit cards over Maneuvers
///         so the CPU builds board presence. Tie-break by lowest cost
///         (curve).</item>
///   <item>For arena picks, prefer an arena where the opponent already
///         has at least one Unit (to force Unit-vs-Unit overlap) —
///         otherwise first uncollapsed opponent conduit.</item>
///   <item>Choice options (Mulligan): pass.</item>
///   <item>Fallback to <c>LegalActions[0]</c>.</item>
/// </list>
/// <para>No look-ahead, no scoring of hypothetical states. Deterministic
/// given the same LegalActions ordering.</para>
/// </summary>
public sealed class FixedLadderBot : IRoomBot
{
    public RtValue Choose(GameState state, InputRequest pending, int cpuEntityId)
    {
        var actions = pending.LegalActions;
        if (actions.Count == 0) return new RtSymbol("pass");

        // 1. Clash: always attack.
        var attack = actions.FirstOrDefault(
            a => a.Kind == "declare_attacker" && a.Label == "attack");
        if (attack is not null) return new RtSymbol(attack.Label);

        // 2. Target: opponent-owned with lowest HP-ish counter > 0.
        if (actions.Any(a => a.Kind == "target_entity"))
        {
            var pick = PickTarget(state, actions, cpuEntityId);
            if (pick is not null) return new RtSymbol(pick.Label);
        }

        // 3. Play card: prefer Unit, tie-break by cost ascending.
        var plays = actions.Where(a => a.Kind == "play_card").ToList();
        if (plays.Count > 0)
        {
            int CostOf(LegalAction a) =>
                int.TryParse(a.Metadata?.GetValueOrDefault("cost") ?? "", out var c) ? c : int.MaxValue;
            bool IsUnit(LegalAction a) => IsUnitPlay(state, a);
            var unitPlays = plays.Where(IsUnit).ToList();
            var pool = unitPlays.Count > 0 ? unitPlays : plays;
            var pick = pool.OrderBy(CostOf).First();
            return new RtSymbol(pick.Label);
        }

        // 4. Arena: prefer overlap with opponent unit, else uncollapsed conduit.
        if (actions.Any(a => a.Kind == "target_arena"))
        {
            var pick = PickArena(state, actions, cpuEntityId);
            if (pick is not null) return new RtSymbol(pick.Label);
        }

        // 5. Mulligan / generic choice — pass if offered, otherwise first.
        var passChoice = actions.FirstOrDefault(a => a.Label == "pass");
        if (passChoice is not null) return new RtSymbol(passChoice.Label);

        return new RtSymbol(actions[0].Label);
    }

    private static LegalAction? PickTarget(
        GameState state,
        IReadOnlyList<LegalAction> actions,
        int cpuEntityId)
    {
        LegalAction? best = null;
        int bestHp = int.MaxValue;
        foreach (var a in actions)
        {
            if (a.Kind != "target_entity") continue;
            if (a.Metadata?.TryGetValue("entityId", out var idStr) != true) continue;
            if (!int.TryParse(idStr, out var id)) continue;
            if (!state.Entities.TryGetValue(id, out var entity)) continue;
            if (entity.OwnerId is null) continue;
            if (entity.OwnerId == cpuEntityId) continue;
            int hp = int.MaxValue;
            foreach (var counter in new[] { "integrity", "current_ramparts", "current_hp" })
            {
                if (!entity.Counters.TryGetValue(counter, out var v)) continue;
                if (v > 0 && v < hp) hp = v;
            }
            if (hp < bestHp || best is null)
            {
                best = a;
                bestHp = hp;
            }
        }
        return best;
    }

    private static bool IsUnitPlay(GameState state, LegalAction action)
    {
        if (action.Metadata?.TryGetValue("type", out var t) == true && t == "Unit") return true;
        if (action.Metadata?.TryGetValue("cardName", out var name) != true || name is null) return false;
        return state.CardDecls.TryGetValue(name, out var decl) && GetCardDeclType(decl) == "Unit";
    }

    private static string? GetCardDeclType(AstCardDecl decl)
    {
        foreach (var f in decl.Body.Fields)
        {
            if (f.Key.Name != "type") continue;
            if (f.Value is AstFieldExpr fe && fe.Value is AstIdent id)
                return id.Name;
        }
        return null;
    }

    private static LegalAction? PickArena(
        GameState state,
        IReadOnlyList<LegalAction> actions,
        int cpuEntityId)
    {
        var arenas = actions.Where(a => a.Kind == "target_arena").ToList();
        if (arenas.Count == 0) return null;

        foreach (var a in arenas)
        {
            if (a.Metadata?.TryGetValue("pos", out var pos) != true || string.IsNullOrEmpty(pos)) continue;
            bool opponentHasUnit = state.Entities.Values.Any(e =>
                e.Kind == "Card" &&
                e.OwnerId is int oid && oid != cpuEntityId &&
                e.Characteristics.TryGetValue("in_play", out var ip) &&
                ip is RtBool rb && rb.V &&
                e.Parameters.TryGetValue("arena", out var arenaParam) &&
                arenaParam is RtSymbol ap && ap.Name == pos);
            if (opponentHasUnit) return a;
        }

        foreach (var a in arenas)
        {
            if (a.Metadata?.TryGetValue("pos", out var pos) != true || string.IsNullOrEmpty(pos)) continue;
            bool conduitStanding = state.Entities.Values.Any(e =>
                e.Kind == "Conduit" &&
                e.OwnerId is int oid && oid != cpuEntityId &&
                !e.Tags.Contains("collapsed") &&
                e.Parameters.TryGetValue("arena", out var arenaParam) &&
                arenaParam is RtSymbol ap && ap.Name == pos);
            if (conduitStanding) return a;
        }
        return arenas[0];
    }
}
