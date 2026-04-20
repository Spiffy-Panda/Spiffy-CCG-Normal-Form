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
    public const int ResonanceFieldCapacity = 5;

    /// <summary>
    /// Read the <c>factions: {...}</c> field off a card declaration as a
    /// flat list of faction name strings. Multi-faction cards push one
    /// echo per faction; single-faction cards push one echo. Returns
    /// empty when the card has no factions field.
    /// </summary>
    public static IReadOnlyList<string> ReadFactions(AstCardDecl decl)
    {
        var result = new List<string>(2);
        foreach (var field in decl.Body.Fields)
        {
            if (field.Key.Name != "factions") continue;
            if (field.Value is not AstFieldExpr fe) continue;
            CollectFactionIdents(fe.Value, result);
            break;
        }
        return result;
    }

    private static void CollectFactionIdents(AstExpr expr, List<string> sink)
    {
        switch (expr)
        {
            case AstIdent id:
                sink.Add(id.Name);
                break;
            case AstBraceExpr be:
                foreach (var entry in be.Entries)
                {
                    if (entry is AstBraceValue bv)
                    {
                        CollectFactionIdents(bv.Value, sink);
                    }
                }
                break;
            case AstListLit ll:
                foreach (var el in ll.Elements)
                {
                    CollectFactionIdents(el, sink);
                }
                break;
        }
    }

    /// <summary>
    /// Append each faction in <paramref name="factions"/> to the player's
    /// ResonanceField (stored as <c>Characteristics["ResonanceField"]</c>
    /// on the player entity, carrying an <see cref="RtList"/> of
    /// <see cref="RtSymbol"/>). The field is a FIFO capped at
    /// <see cref="ResonanceFieldCapacity"/> — every push beyond the cap
    /// evicts the oldest echo.
    /// </summary>
    public static void PushEchoes(Entity player, IEnumerable<string> factions)
    {
        var current = GetResonanceField(player);
        var updated = new List<RtValue>(current);
        foreach (var f in factions)
        {
            updated.Add(new RtSymbol(f));
            while (updated.Count > ResonanceFieldCapacity) updated.RemoveAt(0);
        }
        player.Characteristics["ResonanceField"] = new RtList(updated);
    }

    public static IReadOnlyList<RtValue> GetResonanceField(Entity player)
    {
        if (player.Characteristics.TryGetValue("ResonanceField", out var v) &&
            v is RtList list)
        {
            return list.Elements;
        }
        return Array.Empty<RtValue>();
    }

    public static int CountEcho(Entity player, string faction)
    {
        int count = 0;
        foreach (var echo in GetResonanceField(player))
        {
            if (echo is RtSymbol s && s.Name == faction) count++;
        }
        return count;
    }

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

            foreach (var kwAbility in MakeKeywordAbilities(unit, name, param))
            {
                unit.Abilities.Add(kwAbility);
            }
        }
    }

    private static IEnumerable<AbilityInstance> MakeKeywordAbilities(
        Entity unit, string name, int? param)
    {
        if (TryMakeKeywordAbility(unit, name, param) is AbilityInstance single)
        {
            yield return single;
            yield break;
        }
        foreach (var extra in TryMakeMultiAbilityKeyword(unit, name, param))
        {
            yield return extra;
        }
    }

    private static IEnumerable<AbilityInstance> TryMakeMultiAbilityKeyword(
        Entity unit, string name, int? param)
    {
        var span = Ccgnf.Diagnostics.SourceSpan.Unknown;
        Ast.AstExpr selfIdent = new Ast.AstIdent(span, "self");
        Ast.AstExpr selfController = new Ast.AstMemberAccess(span, selfIdent, "controller");

        Ast.AstFunctionCall MakeEventPattern(string ev, params (string F, Ast.AstExpr V)[] fields)
        {
            var args = new List<Ast.AstArg>(fields.Length);
            foreach (var (f, v) in fields) args.Add(new Ast.AstArgNamed(span, f, v));
            var callee = new Ast.AstMemberAccess(span, new Ast.AstIdent(span, "Event"), ev);
            return new Ast.AstFunctionCall(span, callee, args);
        }

        AbilityInstance MakeTriggeredInline(Ast.AstExpr pattern, Ast.AstExpr effect)
        {
            var nameds = new Dictionary<string, Ast.AstExpr>(StringComparer.Ordinal)
            {
                ["on"] = pattern,
                ["effect"] = effect,
            };
            return new AbilityInstance(AbilityKind.Triggered, unit.Id, nameds, Array.Empty<Ast.AstExpr>());
        }

        switch (name)
        {
            case "Phantom":
            {
                // Keyword_Phantom expands to a StartOfClash Choice.
                // v1 wiring: auto-fade at start of clash, auto-return at
                // end of clash. SetPhantoming zeros the Clash contribution
                // via GetClashProjectedForce / GetClashFortification. At
                // EndOfClash the unit moves back to hand and gets its
                // per-return cost reduction applied.
                var setPhantoming = new Ast.AstFunctionCall(span,
                    new Ast.AstIdent(span, "SetPhantoming"),
                    new List<Ast.AstArg>
                    {
                        new Ast.AstArgPositional(span, selfIdent),
                        new Ast.AstArgPositional(span, new Ast.AstIdent(span, "true")),
                    });
                yield return MakeTriggeredInline(
                    MakeEventPattern("PhaseBegin",
                        ("phase", new Ast.AstIdent(span, "Clash"))),
                    setPhantoming);

                var phantomReturn = new Ast.AstFunctionCall(span,
                    new Ast.AstIdent(span, "PhantomReturn"),
                    new List<Ast.AstArg>
                    {
                        new Ast.AstArgPositional(span, selfIdent),
                    });
                yield return MakeTriggeredInline(
                    MakeEventPattern("PhaseEnd", ("phase", new Ast.AstIdent(span, "Clash"))),
                    phantomReturn);
                yield break;
            }
            case "Drift":
            {
                // EndOfYourTurn drift to a random non-collapsed adjacent
                // arena. v1 simplification: pick randomly rather than
                // prompting the controller (preserves the "drift happens
                // on your end of turn" cadence in bench).
                var driftCall = new Ast.AstFunctionCall(span,
                    new Ast.AstIdent(span, "DriftMoveUnit"),
                    new List<Ast.AstArg>
                    {
                        new Ast.AstArgPositional(span, selfIdent),
                    });
                yield return MakeTriggeredInline(
                    MakeEventPattern("PhaseBegin",
                        ("phase",  new Ast.AstIdent(span, "Fall")),
                        ("player", selfController)),
                    driftCall);
                yield break;
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
            case "Recur":
            {
                // Replacement on Destroy: redirect the move from Cache to
                // bottom_of(Arsenal). The Interpreter's Replacement walker
                // evaluates `replace_with` with `self` bound to this Unit
                // and `owner` bound to its controller.
                var ownerIdent = new Ast.AstIdent(span, "owner");
                var arsenal = new Ast.AstMemberAccess(span, ownerIdent, "Arsenal");
                var bottomCall = new Ast.AstFunctionCall(span,
                    new Ast.AstIdent(span, "bottom_of"),
                    new List<Ast.AstArg>
                    {
                        new Ast.AstArgPositional(span, arsenal),
                    });
                var moveTo = new Ast.AstFunctionCall(span,
                    new Ast.AstIdent(span, "MoveTo"),
                    new List<Ast.AstArg>
                    {
                        new Ast.AstArgPositional(span, selfIdent),
                        new Ast.AstArgPositional(span, bottomCall),
                    });
                var pattern = MakeEventPattern("Destroy", ("target", selfIdent));
                var named = new Dictionary<string, Ast.AstExpr>(StringComparer.Ordinal)
                {
                    ["on"] = pattern,
                    ["replace_with"] = moveTo,
                };
                return new AbilityInstance(
                    AbilityKind.Replacement, unit.Id, named, Array.Empty<Ast.AstExpr>());
            }
            case "Unique":
            {
                // Replacement on EnterPlay(target=self): if the controller
                // already has another in-play copy of this name, redirect
                // into their Cache. The engine handles the guard via the
                // `HasDuplicateInPlay` builtin the Replacement walker
                // evaluates before firing `replace_with`.
                var cacheRef = new Ast.AstMemberAccess(span,
                    new Ast.AstIdent(span, "owner"), "Cache");
                var moveTo = new Ast.AstFunctionCall(span,
                    new Ast.AstIdent(span, "MoveTo"),
                    new List<Ast.AstArg>
                    {
                        new Ast.AstArgPositional(span, selfIdent),
                        new Ast.AstArgPositional(span, cacheRef),
                    });
                var guard = new Ast.AstFunctionCall(span,
                    new Ast.AstIdent(span, "HasDuplicateInPlay"),
                    new List<Ast.AstArg>
                    {
                        new Ast.AstArgPositional(span, selfIdent),
                    });
                var pattern = MakeEventPattern("EnterPlay", ("target", selfIdent));
                var named = new Dictionary<string, Ast.AstExpr>(StringComparer.Ordinal)
                {
                    ["on"] = pattern,
                    ["guard"] = guard,
                    ["replace_with"] = moveTo,
                };
                return new AbilityInstance(
                    AbilityKind.Replacement, unit.Id, named, Array.Empty<Ast.AstExpr>());
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
        if (unit.Tags.Contains("phantoming")) return 0;
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
        // Phantoming Units are off the board for Clash: zero contribution.
        if (unit.Tags.Contains("phantoming")) return 0;
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
