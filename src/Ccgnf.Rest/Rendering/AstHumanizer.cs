using System.Globalization;
using System.Text;
using Ccgnf.Ast;

namespace Ccgnf.Rest.Rendering;

/// <summary>
/// Projects ability expressions from card declarations into human-readable
/// prose for the cards browser.
///
/// v1 strategy:
///   * Top-level ability wrappers (OnResolve, OnEnter, …) dispatch through
///     <see cref="WrapperTemplates"/>; unrecognized wrappers are surfaced
///     as <c>⟪Head(...)⟫</c> so gaps show up in the UI instead of silently
///     producing garbage.
///   * Simple effect calls (DealDamage, Draw, Heal, …) dispatch through
///     <see cref="EffectTemplates"/>.
///   * Combinators (Sequence, If, When, Target, Tiers, ForEach, Static, …)
///     have custom recursive handlers.
///   * Lambdas used as predicates are rendered by collapsing common
///     <c>∧</c>-clauses into adjective phrases ("opposing Unit in this
///     Arena"); unknown clauses fall through to the author's raw source.
///   * Anything still unrecognized is compact-rendered (<c>Head(a, b)</c>).
///
/// Output is intentionally imperfect v1 prose — the golden snapshots under
/// <c>tests/Ccgnf.Rest.Tests/Serialization/AstHumanizerTests.cs</c> pin it.
/// </summary>
public static class AstHumanizer
{
    public static IReadOnlyList<string> HumanizeAbilitiesField(AstFieldValue? fieldValue)
    {
        if (fieldValue is not AstFieldExpr fe) return Array.Empty<string>();
        if (fe.Value is not AstListLit list) return Array.Empty<string>();
        if (list.Elements.Count == 0) return Array.Empty<string>();

        var result = new List<string>(list.Elements.Count);
        foreach (var ability in list.Elements)
            result.Add(HumanizeAbility(ability));
        return result;
    }

    // Top-level ability entry. Wraps the dispatcher so unknown heads show
    // up as ⟪…⟫ — that's the gap marker callers can grep for.
    private static string HumanizeAbility(AstExpr expr)
    {
        if (expr is AstFunctionCall call && call.Callee is AstIdent head)
        {
            if (WrapperTemplates.TryGetValue(head.Name, out var render))
                return render(call).TrimEnd();
        }
        return $"⟪{Compact(expr)}⟫";
    }

    // ---------------- Top-level ability wrappers ----------------

    private static readonly Dictionary<string, Func<AstFunctionCall, string>> WrapperTemplates =
        new()
        {
            ["OnResolve"] = call => $"Resolves: {HumanizeEffect(Arg(call, 0))}.",
            ["OnEnter"] = call => $"When this enters play: {HumanizeEffect(Arg(call, 0))}.",
            ["OnArenaEnter"] = call => $"When this enters an Arena: {HumanizeEffect(Arg(call, 0))}.",
            ["StartOfYourTurn"] = call => $"At start of your turn: {HumanizeEffect(Arg(call, 0))}.",
            ["OnCardPlayed"] = RenderOnCardPlayed,
            ["Triggered"] = RenderTriggered,
            ["Static"] = RenderStatic,
            ["Activated"] = RenderActivated,
        };

    private static string RenderOnCardPlayed(AstFunctionCall call)
    {
        var filter = NamedArg(call, "filter");
        var effect = NamedArg(call, "effect");
        var prefix = filter is null
            ? "When a card is played"
            : $"When a card matching {HumanizePredicate(filter)} is played";
        var body = effect is null ? "(no effect)" : HumanizeEffect(effect);
        return $"{prefix}: {body}.";
    }

    private static string RenderTriggered(AstFunctionCall call)
    {
        var on = NamedArg(call, "on") ?? NamedArg(call, "event");
        var effect = NamedArg(call, "effect") ?? (call.Args.Count > 0 ? Arg(call, call.Args.Count - 1) : null);
        var trigger = on is null ? "trigger" : HumanizeEffect(on);
        var body = effect is null ? "(no effect)" : HumanizeEffect(effect);
        return $"Triggered ({trigger}): {body}.";
    }

    private static string RenderStatic(AstFunctionCall call)
    {
        var modifies = NamedArg(call, "modifies");
        var rule = NamedArg(call, "rule");
        var prefix = modifies is null ? "Static" : $"Static ({HumanizeEffect(modifies)})";
        var body = rule is null ? "(no rule)" : HumanizeEffect(rule);
        return $"{prefix}: {body}.";
    }

    private static string RenderActivated(AstFunctionCall call)
    {
        var cost = NamedArg(call, "cost");
        var effect = NamedArg(call, "effect") ?? (call.Args.Count > 0 ? Arg(call, call.Args.Count - 1) : null);
        var prefix = cost is null ? "Activated" : $"Activated ({HumanizeEffect(cost)})";
        var body = effect is null ? "(no effect)" : HumanizeEffect(effect);
        return $"{prefix}: {body}.";
    }

    // ---------------- Effect / expression dispatch ----------------

    public static string HumanizeEffect(AstExpr? expr) => expr switch
    {
        null => "?",
        AstFunctionCall call => HumanizeCall(call),
        AstIdent id => HumanizeIdent(id.Name),
        AstIntLit i => i.Value.ToString(CultureInfo.InvariantCulture),
        AstStringLit s => s.Value,
        AstBinaryOp b => $"{HumanizeEffect(b.Left)} {HumanizeOp(b.Op)} {HumanizeEffect(b.Right)}",
        AstUnaryOp u => u.Op == "not" ? $"not {HumanizeEffect(u.Operand)}" : $"{u.Op}{HumanizeEffect(u.Operand)}",
        AstMemberAccess m => HumanizeMember(m),
        AstIndex idx => HumanizeIndex(idx),
        AstIfExpr ife => HumanizeIf(ife),
        AstWhenExpr whe => $"when {HumanizeEffect(whe.Predicate)}, {HumanizeEffect(whe.Effect)}",
        AstCondExpr ce => string.Join("; ", ce.Arms.Select(a =>
            a.Predicate is AstIdent { Name: "Default" or "otherwise" }
                ? $"otherwise, {HumanizeEffect(a.Effect)}"
                : $"if {HumanizeEffect(a.Predicate)}, {HumanizeEffect(a.Effect)}")),
        AstSwitchExpr sw => $"based on {HumanizeEffect(sw.Scrutinee)}: "
            + string.Join("; ", sw.Cases.Select(c => $"{c.Label} → {HumanizeEffect(c.Value)}")),
        AstLetExpr le => $"let {le.Variable} = {HumanizeEffect(le.Value)}: {HumanizeEffect(le.Body)}",
        AstListLit list => $"[{string.Join(", ", list.Elements.Select(HumanizeEffect))}]",
        AstBraceExpr brace => HumanizeBrace(brace),
        AstParen p => p.Elements.Count == 1
            ? HumanizeEffect(p.Elements[0])
            : $"({string.Join(", ", p.Elements.Select(HumanizeEffect))})",
        AstLambda lam => HumanizePredicateLambda(lam),
        AstRangeLit r => $"{HumanizeEffect(r.Start)}..{HumanizeEffect(r.End)}",
        _ => Compact(expr),
    };

    private static string HumanizeCall(AstFunctionCall call)
    {
        if (call.Callee is not AstIdent head) return Compact(call);
        var name = head.Name;

        if (CombinatorHandlers.TryGetValue(name, out var handler)) return handler(call);
        if (EffectTemplates.TryGetValue(name, out var template)) return template(call);
        return CompactCall(call);
    }

    // ---------------- Combinators ----------------

    private static readonly Dictionary<string, Func<AstFunctionCall, string>> CombinatorHandlers =
        new()
        {
            ["Sequence"] = RenderSequence,
            ["ForEach"] = RenderForEach,
            ["Target"] = RenderTarget,
            ["Tiers"] = RenderTiers,
            ["When"] = RenderWhen,
            ["Guard"] = RenderGuard,
            ["Repeat"] = RenderRepeat,
            ["Choice"] = RenderChoice,
            ["If"] = call => HumanizeIf(new AstIfExpr(call.Span,
                Arg(call, 0) ?? NoOpExpr(call),
                Arg(call, 1) ?? NoOpExpr(call),
                Arg(call, 2) ?? NoOpExpr(call))),
        };

    private static string RenderSequence(AstFunctionCall call)
    {
        var items = CollapseListArg(call, 0);
        if (items.Count == 0) return "(no effect)";
        if (items.Count == 1) return HumanizeEffect(items[0]);
        return string.Join("; ", items.Select(HumanizeEffect));
    }

    private static string RenderForEach(AstFunctionCall call)
    {
        var lambda = Arg(call, 0) as AstLambda;
        var effect = Arg(call, 1);
        var subject = lambda is null ? "element" : DescribeSubject(lambda);
        var eff = effect is null ? "(nothing)" : HumanizeEffect(effect);
        return $"for each {subject}, {eff}";
    }

    private static string RenderTarget(AstFunctionCall call)
    {
        var lambda = Arg(call, 0) as AstLambda;
        var effect = Arg(call, 1);
        var subject = lambda is null ? "target" : DescribeSubject(lambda);
        var eff = effect is null ? "(nothing)" : HumanizeEffect(effect);
        return $"target {subject}: {eff}";
    }

    private static string RenderTiers(AstFunctionCall call)
    {
        var items = CollapseListArg(call, 0);
        if (items.Count == 0) return "(no effect)";
        var parts = new List<string>();
        foreach (var tier in items)
        {
            // Each tier is a (condition, effect) paren tuple.
            if (tier is AstParen paren && paren.Elements.Count == 2)
            {
                var cond = paren.Elements[0];
                var eff = paren.Elements[1];
                if (cond is AstIdent id && id.Name == "Default")
                    parts.Add($"otherwise, {HumanizeEffect(eff)}");
                else
                    parts.Add($"if {HumanizeEffect(cond)}, {HumanizeEffect(eff)}");
            }
            else
            {
                parts.Add(HumanizeEffect(tier));
            }
        }
        return string.Join("; ", parts);
    }

    private static string RenderWhen(AstFunctionCall call)
    {
        var cond = Arg(call, 0);
        var effect = Arg(call, 1);
        var condText = cond is null ? "triggered" : HumanizeEffect(cond);
        var effText = effect is null ? "(nothing)" : HumanizeEffect(effect);
        return $"when {condText}, {effText}";
    }

    private static string RenderGuard(AstFunctionCall call)
    {
        var cond = Arg(call, 0);
        var effect = Arg(call, 1);
        var condText = cond is null ? "guard" : HumanizeEffect(cond);
        var effText = effect is null ? "(nothing)" : HumanizeEffect(effect);
        return $"only if {condText}, {effText}";
    }

    private static string RenderRepeat(AstFunctionCall call)
    {
        var count = Arg(call, 0);
        var effect = Arg(call, 1);
        var n = count is null ? "" : HumanizeEffect(count);
        var eff = effect is null ? "(nothing)" : HumanizeEffect(effect);
        return $"repeat {n} times: {eff}".Replace("  ", " ");
    }

    private static string RenderChoice(AstFunctionCall call)
    {
        var items = CollapseListArg(call, 0);
        if (items.Count == 0) return "(no choice)";
        return "choose one — " + string.Join(" / ", items.Select(HumanizeEffect));
    }

    private static string HumanizeIf(AstIfExpr ife)
    {
        var cond = HumanizeEffect(ife.Condition);
        var then = HumanizeEffect(ife.Then);
        var els = HumanizeEffect(ife.Else);
        if (IsNoOp(ife.Else)) return $"if {cond}, {then}";
        return $"if {cond}, {then}; otherwise {els}";
    }

    // ---------------- Simple effect templates ----------------

    private static readonly Dictionary<string, Func<AstFunctionCall, string>> EffectTemplates =
        new()
        {
            ["NoOp"] = _ => "do nothing",
            ["DealDamage"] = call => $"deal {HumanizeEffect(Arg(call, 1))} damage to {HumanizeEffect(Arg(call, 0))}",
            ["DistributeDamage"] = call =>
            {
                var amount = NamedArg(call, "amount") ?? Arg(call, 0);
                return $"deal {Render(amount)} damage, split as you choose";
            },
            ["Draw"] = call => $"{HumanizeEffect(Arg(call, 0))} draws {HumanizeEffect(Arg(call, 1))}",
            ["Heal"] = call => $"heal {HumanizeEffect(Arg(call, 1))} on {HumanizeEffect(Arg(call, 0))}",
            ["Pilfer"] = call => $"pilfer {HumanizeEffect(Arg(call, 0))}",
            ["Mend"] = call => $"mend {HumanizeEffect(Arg(call, 0))}",
            ["Ignite"] = call => $"ignite {HumanizeEffect(Arg(call, 0))}",
            ["Shuffle"] = call => $"shuffle {HumanizeEffect(Arg(call, 0))}",
            ["RefillAether"] = _ => "refill aether",
            ["PayAether"] = call => $"pay {HumanizeEffect(Arg(call, 0))} aether",
            ["EmitEvent"] = call => $"emit {HumanizeEffect(Arg(call, 0))}",
            ["MoveTo"] = call => $"move {HumanizeEffect(Arg(call, 0))} to {HumanizeEffect(Arg(call, 1))}",
            ["GrantKeyword"] = call =>
            {
                var tgt = HumanizeEffect(Arg(call, 0));
                var kw = HumanizeEffect(Arg(call, 1));
                var dur = NamedArg(call, "duration");
                var suffix = dur is null ? "" : $" until {HumanizeEffect(dur)}";
                return $"grant {kw} to {tgt}{suffix}";
            },
            ["SetKeywordParameter"] = call =>
                $"set {HumanizeEffect(Arg(call, 1))} on {HumanizeEffect(Arg(call, 0))} to {HumanizeEffect(Arg(call, 2))}",
            ["KeywordParameter"] = call =>
                $"{HumanizeEffect(Arg(call, 1))} on {HumanizeEffect(Arg(call, 0))}",
            ["AddToCharacteristic"] = call =>
            {
                var n = Arg(call, 2);
                var sign = n is AstIntLit i && i.Value > 0 ? "+" : "";
                return $"{HumanizeEffect(Arg(call, 0))} gets {sign}{HumanizeEffect(n)} {HumanizeEffect(Arg(call, 1))}";
            },
            ["SetCharacteristic"] = call =>
                $"set {HumanizeEffect(Arg(call, 1))} on {HumanizeEffect(Arg(call, 0))} to {HumanizeEffect(Arg(call, 2))}",
            ["Characteristic"] = call =>
                $"{HumanizeEffect(Arg(call, 1))} on {HumanizeEffect(Arg(call, 0))}",
            ["Resonance"] = call => $"{HumanizeEffect(Arg(call, 0))} {HumanizeEffect(Arg(call, 1))}",
            ["Peak"] = call => $"Peak {HumanizeEffect(Arg(call, 0))}",
            ["Exists"] = call => $"{HumanizeEffect(Arg(call, 0))} exists",
            ["BannerExists"] = _ => "your Banner exists",
            ["Count"] = call => $"count of {HumanizeEffect(Arg(call, 0))}",
            ["other_player"] = call => $"the other player of {HumanizeEffect(Arg(call, 0))}",
            ["collapsed_for"] = call =>
                $"{HumanizeEffect(Arg(call, 1))} has collapsed for {HumanizeEffect(Arg(call, 0))}",
            ["PlayAsCopy"] = _ => "play a copy of the triggering card",
            ["InstantiateEntity"] = call => $"create {HumanizeEffect(Arg(call, 0))}",
        };

    // ---------------- Identifiers + members ----------------

    private static string HumanizeIdent(string name) => name switch
    {
        "self" => "this",
        "target" => "target",
        "Default" => "otherwise",
        "continuously" => "continuously",
        "end_of_turn" => "end of turn",
        _ => name,
    };

    private static string HumanizeMember(AstMemberAccess m)
    {
        var rendered = Compact(m);
        return rendered switch
        {
            "self.controller" => "you",
            "self.arena" => "this Arena",
            "event.card" => "the triggering card",
            "event.arena" => "the triggering Arena",
            "event.targets" => "the triggered targets",
            _ => rendered,
        };
    }

    private static string HumanizeIndex(AstIndex idx) => Compact(idx);

    private static string HumanizeBrace(AstBraceExpr brace)
    {
        var parts = new List<string>(brace.Entries.Count);
        foreach (var e in brace.Entries)
        {
            if (e is AstBraceValue bv) parts.Add(HumanizeEffect(bv.Value));
            else if (e is AstBraceField bf)
            {
                var inner = bf.Field.Value is AstFieldExpr fe ? HumanizeEffect(fe.Value) : "…";
                parts.Add($"{bf.Field.Key.Name}: {inner}");
            }
        }
        return $"{{{string.Join(", ", parts)}}}";
    }

    private static string HumanizeOp(string op) => op switch
    {
        "==" => "is",
        "!=" => "is not",
        "∧" or "&&" => "and",
        "∨" or "||" => "or",
        "∈" => "in",
        _ => op,
    };

    // ---------------- Lambdas / predicates ----------------

    // For predicates used as filters / targets, produce a noun phrase like
    // "opposing Unit in this Arena". Fallback to the raw lambda body.
    private static string DescribeSubject(AstLambda lam)
    {
        var clauses = FlattenAnd(lam.Body);
        var flags = new PredicateFlags();
        var residual = new List<AstExpr>();
        foreach (var c in clauses)
        {
            if (!TryMatchPredicateClause(c, flags)) residual.Add(c);
        }
        var phrase = flags.ToPhrase();
        if (residual.Count > 0)
        {
            var extra = string.Join(" and ", residual.Select(r => HumanizeEffect(r)));
            phrase = phrase.Length == 0 ? extra : $"{phrase} where {extra}";
        }
        return phrase.Length == 0 ? "target" : phrase;
    }

    private static string HumanizePredicateLambda(AstLambda lam) => DescribeSubject(lam);

    private static string HumanizePredicate(AstExpr expr)
    {
        if (expr is AstLambda lam) return DescribeSubject(lam);
        return HumanizeEffect(expr);
    }

    private sealed class PredicateFlags
    {
        public string? Ownership;  // "friendly" | "opposing"
        public string? Type;       // "Unit" | "Maneuver" | ...
        public string? Kind;       // "Conduit" | "Arena" | ... or "Unit or Conduit"
        public bool InThisArena;
        public string? Faction;    // "EMBER", etc.
        public bool NotCollapsed;

        public string ToPhrase()
        {
            var sb = new StringBuilder();
            if (Faction is not null) sb.Append(Faction).Append(' ');
            if (Ownership is not null) sb.Append(Ownership).Append(' ');
            if (Type is not null) sb.Append(Type);
            else if (Kind is not null) sb.Append(Kind);
            if (sb.Length == 0) sb.Append("target");
            if (InThisArena) sb.Append(" in this Arena");
            if (NotCollapsed) sb.Append(" (not collapsed)");
            return sb.ToString().Trim();
        }
    }

    private static bool TryMatchPredicateClause(AstExpr expr, PredicateFlags flags)
    {
        // x.controller == self.controller  →  friendly
        // x.controller != self.controller  →  opposing
        if (expr is AstBinaryOp bop)
        {
            var left = Compact(bop.Left);
            var right = Compact(bop.Right);
            if (right == "self.controller" && left.EndsWith(".controller"))
            {
                if (bop.Op == "==") { flags.Ownership = "friendly"; return true; }
                if (bop.Op == "!=") { flags.Ownership = "opposing"; return true; }
            }
            if (right == "self.arena" && left.EndsWith(".arena") && bop.Op == "==")
            {
                flags.InThisArena = true; return true;
            }
            // x.type == Unit
            if (left.EndsWith(".type") && bop.Op == "==" && bop.Right is AstIdent tid)
            {
                flags.Type = tid.Name; return true;
            }
            // x.kind == Conduit
            if (left.EndsWith(".kind") && bop.Op == "==" && bop.Right is AstIdent kid)
            {
                flags.Kind = kid.Name; return true;
            }
            // x.kind ∈ {Unit, Conduit}
            if (left.EndsWith(".kind") && bop.Op == "∈" && bop.Right is AstBraceExpr brace)
            {
                var names = new List<string>();
                foreach (var entry in brace.Entries)
                    if (entry is AstBraceValue bv && bv.Value is AstIdent id) names.Add(id.Name);
                if (names.Count > 0) { flags.Kind = string.Join(" or ", names); return true; }
            }
            // EMBER ∈ x.factions
            if (bop.Op == "∈" && bop.Left is AstIdent fid && bop.Right is AstMemberAccess ma
                && ma.Member == "factions")
            {
                flags.Faction = fid.Name; return true;
            }
            // x != self
            if (bop.Op == "!=" && bop.Right is AstIdent s && s.Name == "self") return true;
        }
        // not collapsed_for(self.controller, x)
        if (expr is AstUnaryOp uop && uop.Op == "not"
            && uop.Operand is AstFunctionCall coll
            && coll.Callee is AstIdent cname && cname.Name == "collapsed_for")
        {
            flags.NotCollapsed = true; return true;
        }
        return false;
    }

    private static List<AstExpr> FlattenAnd(AstExpr expr)
    {
        var acc = new List<AstExpr>();
        void Walk(AstExpr e)
        {
            if (e is AstBinaryOp b && (b.Op == "∧" || b.Op == "&&")) { Walk(b.Left); Walk(b.Right); }
            else acc.Add(e);
        }
        Walk(expr);
        return acc;
    }

    // ---------------- Helpers ----------------

    private static AstExpr? Arg(AstFunctionCall call, int index)
    {
        if (index < 0 || index >= call.Args.Count) return null;
        return call.Args[index] switch
        {
            AstArgPositional p => p.Value,
            AstArgNamed n => n.Value,
            AstArgBinding b => b.Value,
            _ => null,
        };
    }

    private static AstExpr? NamedArg(AstFunctionCall call, string name)
    {
        foreach (var a in call.Args)
        {
            if (a is AstArgNamed n && n.Name == name) return n.Value;
            if (a is AstArgBinding b && b.Name == name) return b.Value;
        }
        return null;
    }

    private static IReadOnlyList<AstExpr> CollapseListArg(AstFunctionCall call, int index)
    {
        var arg = Arg(call, index);
        if (arg is AstListLit list) return list.Elements;
        if (arg is null) return Array.Empty<AstExpr>();
        return new[] { arg };
    }

    private static bool IsNoOp(AstExpr? expr) =>
        expr is AstIdent { Name: "NoOp" }
        || (expr is AstFunctionCall fc && fc.Callee is AstIdent id && id.Name == "NoOp");

    private static string Render(AstExpr? expr) => expr is null ? "?" : HumanizeEffect(expr);

    private static AstExpr NoOpExpr(AstFunctionCall call) => new AstIdent(call.Span, "NoOp");

    // Compact render: deterministic structural echo of the AST, no English.
    // Used for unknown nodes so the output is at least traceable.
    private static string Compact(AstExpr expr)
    {
        var sb = new StringBuilder();
        CompactInto(expr, sb);
        return sb.ToString();
    }

    private static void CompactInto(AstExpr expr, StringBuilder sb)
    {
        switch (expr)
        {
            case AstIntLit i: sb.Append(i.Value.ToString(CultureInfo.InvariantCulture)); break;
            case AstStringLit s: sb.Append('"').Append(s.Value).Append('"'); break;
            case AstIdent id: sb.Append(id.Name); break;
            case AstMemberAccess m:
                CompactInto(m.Target, sb); sb.Append('.').Append(m.Member); break;
            case AstIndex idx:
                CompactInto(idx.Target, sb); sb.Append('[');
                for (int i = 0; i < idx.Indices.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    CompactInto(idx.Indices[i], sb);
                }
                sb.Append(']'); break;
            case AstBinaryOp b:
                CompactInto(b.Left, sb); sb.Append(' ').Append(b.Op).Append(' '); CompactInto(b.Right, sb); break;
            case AstUnaryOp u:
                sb.Append(u.Op); if (u.Op == "not") sb.Append(' '); CompactInto(u.Operand, sb); break;
            case AstFunctionCall c:
                CompactCallInto(c, sb); break;
            case AstListLit list:
                sb.Append('['); for (int i = 0; i < list.Elements.Count; i++)
                { if (i > 0) sb.Append(", "); CompactInto(list.Elements[i], sb); }
                sb.Append(']'); break;
            case AstBraceExpr brace:
                sb.Append('{'); for (int i = 0; i < brace.Entries.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    if (brace.Entries[i] is AstBraceValue bv) CompactInto(bv.Value, sb);
                    else sb.Append("…");
                }
                sb.Append('}'); break;
            case AstParen p:
                sb.Append('('); for (int i = 0; i < p.Elements.Count; i++)
                { if (i > 0) sb.Append(", "); CompactInto(p.Elements[i], sb); }
                sb.Append(')'); break;
            case AstLambda lam:
                sb.Append(string.Join(", ", lam.Parameters)).Append(" -> "); CompactInto(lam.Body, sb); break;
            case AstIfExpr ife:
                sb.Append("If("); CompactInto(ife.Condition, sb); sb.Append(", ");
                CompactInto(ife.Then, sb); sb.Append(", "); CompactInto(ife.Else, sb); sb.Append(')'); break;
            case AstWhenExpr whe:
                sb.Append("When("); CompactInto(whe.Predicate, sb); sb.Append(", ");
                CompactInto(whe.Effect, sb); sb.Append(')'); break;
            case AstCondExpr ce:
                sb.Append("Cond(");
                for (int i = 0; i < ce.Arms.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    CompactInto(ce.Arms[i].Predicate, sb); sb.Append(" → ");
                    CompactInto(ce.Arms[i].Effect, sb);
                }
                sb.Append(')'); break;
            case AstSwitchExpr sw:
                sb.Append("Switch("); CompactInto(sw.Scrutinee, sb); sb.Append(")"); break;
            case AstLetExpr le:
                sb.Append("let ").Append(le.Variable).Append(" = ");
                CompactInto(le.Value, sb); sb.Append(" in "); CompactInto(le.Body, sb); break;
            case AstRangeLit r:
                CompactInto(r.Start, sb); sb.Append(".."); CompactInto(r.End, sb); break;
            default: sb.Append("…"); break;
        }
    }

    private static string CompactCall(AstFunctionCall call)
    {
        var sb = new StringBuilder();
        CompactCallInto(call, sb);
        return sb.ToString();
    }

    private static void CompactCallInto(AstFunctionCall call, StringBuilder sb)
    {
        CompactInto(call.Callee, sb);
        sb.Append('(');
        for (int i = 0; i < call.Args.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            switch (call.Args[i])
            {
                case AstArgPositional p: CompactInto(p.Value, sb); break;
                case AstArgNamed n: sb.Append(n.Name).Append(": "); CompactInto(n.Value, sb); break;
                case AstArgBinding b: sb.Append(b.Name).Append('='); CompactInto(b.Value, sb); break;
            }
        }
        sb.Append(')');
    }
}
