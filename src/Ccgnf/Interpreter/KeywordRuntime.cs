using Ccgnf.Ast;

namespace Ccgnf.Interpreter;

/// <summary>
/// Runtime helpers for the keyword macros declared in
/// <c>encoding/engine/03-keyword-macros.ccgnf</c>. v1's interpreter does not
/// yet evaluate <c>Static(...)</c> / <c>Triggered(...)</c> AST on non-Game
/// entities, so each wired keyword's semantics are implemented here as direct
/// C# helpers that consult the Unit's stored keyword list.
/// <para>
/// A Unit's keyword set lives on <see cref="Entity.Tags"/> (one tag per
/// keyword name); parameterised keywords store their numeric argument on
/// <see cref="Entity.Counters"/> under <c>kw:&lt;name&gt;</c>. This keeps the
/// representation flat and cheap to inspect — good enough until the full
/// layered-static-ability evaluator lands.
/// </para>
/// </summary>
public static class KeywordRuntime
{
    public const string TagPrefix = "kw:";

    /// <summary>
    /// Read the <c>keywords: [ ... ]</c> field from a card declaration and
    /// return a flat list of (name, optional int parameter). Non-integer
    /// arguments (e.g. <c>Kindle(effect: ...)</c>) are ignored for v1 — the
    /// macro itself is not wired so we record only the keyword's presence.
    /// </summary>
    public static IReadOnlyList<(string Name, int? Param)> ReadKeywords(AstCardDecl decl)
    {
        var result = new List<(string, int?)>();
        foreach (var field in decl.Body.Fields)
        {
            if (field.Key.Name != "keywords") continue;
            if (field.Value is not AstFieldExpr fe) continue;
            if (fe.Value is not AstListLit list) continue;
            foreach (var el in list.Elements)
            {
                switch (el)
                {
                    case AstIdent id:
                        result.Add((id.Name, null));
                        break;
                    case AstFunctionCall call when call.Callee is AstIdent callee:
                        int? param = null;
                        if (call.Args.Count > 0 && call.Args[0] is AstArgPositional pa
                            && pa.Value is AstIntLit intLit)
                        {
                            param = intLit.Value;
                        }
                        result.Add((callee.Name, param));
                        break;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Attach the given keywords to a runtime Unit. Called from
    /// <c>PlaceUnit</c> right after the Unit's stats are copied from its card
    /// declaration.
    /// </summary>
    public static void ApplyKeywords(Entity unit, IReadOnlyList<(string Name, int? Param)> keywords)
    {
        foreach (var (name, param) in keywords)
        {
            unit.Tags.Add(TagPrefix + name);
            if (param is int n)
            {
                // Stacked parameterised keywords (same name twice — rare,
                // but Mend 2 + Mend 1 is possible via auras) accumulate.
                var key = "kw_param_" + name;
                unit.Counters[key] = unit.Counters.GetValueOrDefault(key, 0) + n;
            }
        }
    }

    public static bool HasKeyword(Entity unit, string name) =>
        unit.Tags.Contains(TagPrefix + name);

    /// <summary>
    /// Sum of the numeric parameter across all Fortify occurrences on the Unit
    /// (cards rarely stack two, but <c>CohortCaptain</c>'s Fortify-grant aura
    /// would add to an intrinsic Fortify). Zero if the Unit has no Fortify.
    /// </summary>
    public static int GetFortifyAmount(Entity unit) =>
        unit.Counters.GetValueOrDefault("kw_param_Fortify", 0);

    /// <summary>
    /// Ramparts the Unit is projecting for purposes of Clash Fortification —
    /// base <c>current_ramparts</c> plus any active Fortify bonus (gated on
    /// the controller's Conduit in this Arena being at integrity ≥ 4, per
    /// <c>Keyword_Fortify</c>). The bonus is a runtime re-evaluation: we do
    /// NOT mutate the Unit's <c>current_ramparts</c>.
    /// </summary>
    public static int EffectiveRamparts(Entity unit, GameState state)
    {
        int basis = unit.Counters.GetValueOrDefault("current_ramparts", 0);
        int fortify = GetFortifyAmount(unit);
        if (fortify == 0) return basis;

        if (!unit.Parameters.TryGetValue("arena", out var arenaVal) ||
            arenaVal is not RtSymbol arenaSym)
        {
            return basis;
        }
        if (unit.OwnerId is not int ownerId) return basis;

        int conduitIntegrity = FindOwnerConduitIntegrity(state, ownerId, arenaSym.Name);
        return conduitIntegrity >= 4 ? basis + fortify : basis;
    }

    /// <summary>
    /// Projected-Force contribution at Clash. Sentinel overrides to 0; every
    /// other unit projects its raw <c>force</c> counter. Deployment Sickness
    /// would zero this too, but that keyword's own dispatch is still unwired
    /// (see §3 of <c>docs/plan/engine-completion-guide.md</c>).
    /// </summary>
    public static int GetClashProjectedForce(Entity unit, GameState state)
    {
        _ = state;
        if (HasKeyword(unit, "Sentinel")) return 0;
        return unit.Counters.GetValueOrDefault("force", 0);
    }

    /// <summary>
    /// Per-Unit Fortification contribution on its controller's side of the
    /// Arena: effective Ramparts plus any Sentinel redirect of the Unit's
    /// Force into Fortification.
    /// </summary>
    public static int GetClashFortification(Entity unit, GameState state)
    {
        int basis = EffectiveRamparts(unit, state);
        if (HasKeyword(unit, "Sentinel"))
        {
            basis += unit.Counters.GetValueOrDefault("force", 0);
        }
        return basis;
    }

    private static int FindOwnerConduitIntegrity(GameState state, int ownerId, string arenaPos)
    {
        foreach (var e in state.Entities.Values)
        {
            if (e.Kind != "Conduit") continue;
            if (e.OwnerId != ownerId) continue;
            if (e.Tags.Contains("collapsed")) continue;
            if (!e.Parameters.TryGetValue("arena", out var av) ||
                av is not RtSymbol ap || ap.Name != arenaPos)
            {
                continue;
            }
            return e.Counters.GetValueOrDefault("integrity", 0);
        }
        return 0;
    }
}
