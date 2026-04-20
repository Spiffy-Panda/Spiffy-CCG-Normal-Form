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
    /// declaration. Also synthesises Triggered abilities for the keywords
    /// whose macros in <c>encoding/engine/03-keyword-macros.ccgnf</c> have
    /// been lifted into this runtime (Mend, Rally, Ignite). Keywords that
    /// still need ResonanceField / Replacement dispatch (Phantom, Recur,
    /// Shroud, Surge, Reshape, Kindle) are stamped into the tag set but
    /// stay silent at runtime; see step 13 for the remaining work.
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

            if (TryMakeKeywordAbility(unit, name, param) is AbilityInstance kwAbility)
            {
                unit.Abilities.Add(kwAbility);
            }
        }
    }

    /// <summary>
    /// Synthesise the Triggered ability the keyword macro expands to, so
    /// <c>Interpreter.DispatchEvent</c> walks it alongside the card's
    /// authored abilities. Returns null for keywords that aren't lifted
    /// into this runtime shape yet.
    /// </summary>
    private static AbilityInstance? TryMakeKeywordAbility(Entity unit, string name, int? param)
    {
        var span = Ccgnf.Diagnostics.SourceSpan.Unknown;
        Ast.AstExpr selfIdent = new Ast.AstIdent(span, "self");
        Ast.AstExpr selfController = new Ast.AstMemberAccess(span, selfIdent, "controller");

        Ast.AstFunctionCall MakeEventPattern(string ev, params (string F, Ast.AstExpr V)[] fields)
        {
            var args = new List<Ast.AstArg>(fields.Length);
            foreach (var (f, v) in fields)
            {
                args.Add(new Ast.AstArgNamed(span, f, v));
            }
            var callee = new Ast.AstMemberAccess(span, new Ast.AstIdent(span, "Event"), ev);
            return new Ast.AstFunctionCall(span, callee, args);
        }

        switch (name)
        {
            case "Mend":
            {
                if (param is not int amount) return null;
                // Keyword_Mend(X) heals the controller's Conduit in this
                // Arena by X on entry, capped at starting integrity. The
                // interim `HealSelfArenaConduit` builtin packages that
                // lookup so we don't have to evaluate a
                // `self.controller.Conduit(self.arena)` path.
                var effect = new Ast.AstFunctionCall(span,
                    new Ast.AstIdent(span, "HealSelfArenaConduit"),
                    new List<Ast.AstArg>
                    {
                        new Ast.AstArgPositional(span, selfIdent),
                        new Ast.AstArgPositional(span, new Ast.AstIntLit(span, amount)),
                    });
                return MakeTriggered(unit,
                    MakeEventPattern("EnterPlay", ("target", selfIdent)), effect);
            }
            case "Rally":
            {
                // On another friendly Unit entering this Unit's arena, buff
                // self's Force by 1. Pattern: Event.EnterPlay(target=t,
                // arena=self.arena). Inline guard keeps us from self-firing.
                var selfArena = new Ast.AstMemberAccess(span, selfIdent, "arena");
                var effect = new Ast.AstFunctionCall(span,
                    new Ast.AstIdent(span, "IncCounter"),
                    new List<Ast.AstArg>
                    {
                        new Ast.AstArgPositional(span, selfIdent),
                        new Ast.AstArgPositional(span, new Ast.AstIdent(span, "force")),
                        new Ast.AstArgPositional(span, new Ast.AstIntLit(span, 1)),
                    });
                var guard = new Ast.AstBinaryOp(span, "!=",
                    new Ast.AstIdent(span, "t"), selfIdent);
                var guarded = new Ast.AstFunctionCall(span,
                    new Ast.AstIdent(span, "If"),
                    new List<Ast.AstArg>
                    {
                        new Ast.AstArgPositional(span, guard),
                        new Ast.AstArgPositional(span, effect),
                        new Ast.AstArgPositional(span, new Ast.AstFunctionCall(
                            span, new Ast.AstIdent(span, "NoOp"), Array.Empty<Ast.AstArg>())),
                    });
                return MakeTriggered(unit,
                    MakeEventPattern("EnterPlay",
                        ("target", new Ast.AstIdent(span, "t")),
                        ("arena",  selfArena)),
                    guarded);
            }
            case "Ignite":
            {
                if (param is not int dmg) return null;
                // Keyword_Ignite(X) pings opposing low-Ramparts Units in
                // the Arena at start of your turn. Interim implementation
                // chips the opposing Conduit in this Arena instead, which
                // preserves the keyword's "per-arena start-of-turn
                // pressure" feel without a ForEach-lambda path yet.
                var effect = new Ast.AstFunctionCall(span,
                    new Ast.AstIdent(span, "IgniteTickArenaConduit"),
                    new List<Ast.AstArg>
                    {
                        new Ast.AstArgPositional(span, selfIdent),
                        new Ast.AstArgPositional(span, new Ast.AstIntLit(span, dmg)),
                    });
                return MakeTriggered(unit,
                    MakeEventPattern("PhaseBegin",
                        ("phase",  new Ast.AstIdent(span, "Rise")),
                        ("player", selfController)),
                    effect);
            }
            default:
                return null;
        }
    }

    private static AbilityInstance MakeTriggered(Entity unit, Ast.AstExpr pattern, Ast.AstExpr effect)
    {
        var named = new Dictionary<string, Ast.AstExpr>(StringComparer.Ordinal)
        {
            ["on"] = pattern,
            ["effect"] = effect,
        };
        return new AbilityInstance(AbilityKind.Triggered, unit.Id, named, Array.Empty<Ast.AstExpr>());
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
