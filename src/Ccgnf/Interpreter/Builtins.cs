using Ccgnf.Ast;
using Microsoft.Extensions.Logging;

namespace Ccgnf.Interpreter;

/// <summary>
/// Dispatch table for builtin functions the v1 interpreter understands. Each
/// builtin receives the raw <see cref="AstFunctionCall"/> so it can decide
/// which argument subtrees to evaluate eagerly (value ops: everything) and
/// which to keep as AST for control-flow (Sequence, ForEach, If, Choice,
/// Repeat). Unknown callees fall through to the caller, which treats them as
/// inert in v1 (macro gaps, card-text helpers not yet wired).
/// </summary>
internal static class Builtins
{
    public static bool TryDispatch(
        string name,
        AstFunctionCall call,
        RtEnv env,
        Evaluator ev,
        out RtValue result)
    {
        switch (name)
        {
            // ----- Control flow -----
            case "Sequence":      result = Sequence(call, env, ev); return true;
            case "ForEach":       result = ForEach(call, env, ev); return true;
            case "Repeat":        result = Repeat(call, env, ev); return true;
            case "Choice":        result = Choice(call, env, ev); return true;
            case "NoOp":          result = new RtNoOp(); return true;
            case "Guard":         result = new RtVoid(); return true;

            // ----- Sets and lists -----
            case "Count":         result = CountOf(EvalFirst(call, env, ev), ev); return true;
            case "Max":           result = Max(call, env, ev); return true;
            case "Min":           result = Min(call, env, ev); return true;

            // ----- Event ops -----
            case "EmitEvent":     result = EmitEvent(call, env, ev); return true;

            // ----- Counter / characteristic ops -----
            case "SetCounter":    result = SetCounter(call, env, ev); return true;
            case "IncCounter":    result = IncCounter(call, env, ev); return true;
            case "ClearCounter":  result = ClearCounter(call, env, ev); return true;
            case "SetCharacteristic": result = SetCharacteristic(call, env, ev); return true;
            case "SetFlag":       result = SetFlag(call, env, ev); return true;

            // ----- Draw / shuffle / deck ops -----
            case "Shuffle":       result = Shuffle(call, env, ev); return true;
            case "Draw":          result = Draw(call, env, ev); return true;

            // ----- Randomness / instantiation -----
            case "RandomChoose":      result = RandomChoose(call, env, ev); return true;
            case "InstantiateEntity": result = InstantiateEntity(call, env, ev); return true;

            // ----- Aether / resource ops -----
            case "RefillAether":  result = RefillAether(call, env, ev); return true;
            case "PayAether":     result = PayAether(call, env, ev); return true;

            // ----- Player / turn helpers -----
            case "other_player":  result = OtherPlayer(call, env, ev); return true;
            case "TurnOrderFrom": result = TurnOrderFrom(call, env, ev); return true;

            // ----- Ability framework stubs — return empty/void in v1 -----
            case "abilities_of_permanents": result = new RtList(Array.Empty<RtValue>()); return true;
            case "OpenTimingWindow":        result = new RtVoid(); return true;
            case "DrainTriggersFor":        result = new RtVoid(); return true;
            case "BeginPhase":              result = new RtVoid(); return true;
            case "EnterMainPhase":          result = new RtVoid(); return true;
            case "ResolveClashPhase":       result = new RtVoid(); return true;

            // ----- Target / Mulligan helpers — v1 relies on "pass" inputs, never invoked -----
            case "Target":                  result = new RtVoid(); return true;
            case "PerformMulligan":         result = new RtVoid(); return true;
            case "MoveTo":                  result = new RtVoid(); return true;

            default:
                result = null!;
                return false;
        }
    }

    // -------------------------------------------------------------------------
    // Control flow
    // -------------------------------------------------------------------------

    private static RtValue Sequence(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        var arg = FirstPositional(call);
        if (arg is AstListLit list)
        {
            foreach (var item in list.Elements)
            {
                ev.Eval(item, env);
            }
            return new RtVoid();
        }
        // Sequence over a single non-list element is just that element.
        if (arg is not null) ev.Eval(arg, env);
        return new RtVoid();
    }

    private static RtValue Repeat(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 2) return new RtVoid();
        int n = Evaluator.AsInt(ev.Eval(ExprOf(call.Args[0]), env));
        var body = ExprOf(call.Args[1]);
        for (int i = 0; i < n; i++) ev.Eval(body, env);
        return new RtVoid();
    }

    private static RtValue ForEach(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        // Two supported shapes:
        //   ForEach(p ∈ S, effect)                        — bind p over S
        //   ForEach((p, a) ∈ S1 × S2, effect)             — bind tuple over product
        //   ForEach(u -> pred(u), effect)                 — lambda predicate over all entities
        //                                                   (not exercised by Setup; stub).
        if (call.Args.Count < 2) return new RtVoid();
        var binding = ExprOf(call.Args[0]);
        var body = ExprOf(call.Args[1]);

        if (binding is AstBinaryOp bin && (bin.Op == "in" || bin.Op == "∈"))
        {
            var source = ev.Eval(bin.Right, env);
            var items = Evaluator.AsList(source);

            if (bin.Left is AstParen paren && paren.Elements.All(e => e is AstIdent))
            {
                var names = paren.Elements.OfType<AstIdent>().Select(i => i.Name).ToList();
                foreach (var item in items)
                {
                    if (item is RtTuple t && t.Elements.Count >= names.Count)
                    {
                        var frame = new List<(string, RtValue)>(names.Count);
                        for (int i = 0; i < names.Count; i++) frame.Add((names[i], t.Elements[i]));
                        ev.Eval(body, env.Extend(frame));
                    }
                }
                return new RtVoid();
            }

            if (bin.Left is AstIdent idVar)
            {
                foreach (var item in items)
                {
                    ev.Eval(body, env.Extend(idVar.Name, item));
                }
                return new RtVoid();
            }
        }

        // Lambda-predicate form: not exercised in Setup; no-op in v1.
        return new RtVoid();
    }

    private static RtValue Choice(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        // Choice(chooser: p, options: { key1: effect1, key2: effect2, ... })
        AstExpr? optionsExpr = null;
        string? chooserLabel = null;
        foreach (var a in call.Args)
        {
            switch (a)
            {
                case AstArgNamed { Name: "options" } n: optionsExpr = n.Value; break;
                case AstArgNamed { Name: "chooser" } n2: chooserLabel = Describe(n2.Value); break;
            }
        }
        if (optionsExpr is not AstBraceExpr be) return new RtVoid();

        var choice = ev.Scheduler.Inputs.Next($"Choice({chooserLabel ?? "?"})");
        var choiceKey = choice switch
        {
            RtSymbol s => s.Name,
            RtString s => s.V,
            _ => choice.ToString() ?? "",
        };
        foreach (var entry in be.Entries)
        {
            if (entry is AstBraceField bf && bf.Field.Key.Name == choiceKey)
            {
                return ev.Eval(ExprOfFieldValue(bf.Field.Value), env);
            }
        }
        ev.Log.LogDebug("Choice: no option matched key {Key}", choiceKey);
        return new RtVoid();
    }

    // -------------------------------------------------------------------------
    // Value ops
    // -------------------------------------------------------------------------

    private static RtValue CountOf(RtValue v, Evaluator ev)
    {
        if (v is RtZoneRef zr
            && ev.State.Entities.TryGetValue(zr.OwnerId, out var owner)
            && owner.Zones.TryGetValue(zr.Name, out var zone))
        {
            return new RtInt(zone.Contents.Count);
        }
        return new RtInt(Evaluator.AsList(v).Count);
    }

    private static RtValue Max(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        int? best = null;
        foreach (var a in call.Args)
        {
            int v = Evaluator.AsInt(ev.Eval(ExprOf(a), env));
            best = best is null ? v : Math.Max(best.Value, v);
        }
        return new RtInt(best ?? 0);
    }

    private static RtValue Min(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        int? best = null;
        foreach (var a in call.Args)
        {
            int v = Evaluator.AsInt(ev.Eval(ExprOf(a), env));
            best = best is null ? v : Math.Min(best.Value, v);
        }
        return new RtInt(best ?? 0);
    }

    private static RtValue OtherPlayer(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        var arg = ev.Eval(ExprOf(call.Args[0]), env);
        if (arg is RtEntityRef er)
        {
            foreach (var p in ev.State.Players)
            {
                if (p.Id != er.Id) return new RtEntityRef(p.Id);
            }
        }
        return new RtVoid();
    }

    private static RtValue TurnOrderFrom(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        var first = ev.Eval(ExprOf(call.Args[0]), env);
        if (first is RtEntityRef er)
        {
            var others = ev.State.Players.Where(p => p.Id != er.Id).Select(p => new RtEntityRef(p.Id));
            var list = new List<RtValue> { new RtEntityRef(er.Id) };
            list.AddRange(others);
            return new RtList(list);
        }
        return new RtList(ev.State.Players.Select(p => (RtValue)new RtEntityRef(p.Id)).ToList());
    }

    // -------------------------------------------------------------------------
    // Event ops
    // -------------------------------------------------------------------------

    private static RtValue EmitEvent(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count == 0) return new RtVoid();
        var payload = ev.Eval(ExprOf(call.Args[0]), env);
        if (payload is RtEventLit elit)
        {
            var gameEvent = new GameEvent(elit.TypeName, new Dictionary<string, RtValue>(elit.Fields));
            ev.State.PendingEvents.Enqueue(gameEvent);
            ev.Log.LogDebug("EmitEvent {Event}", gameEvent);
        }
        return new RtVoid();
    }

    // -------------------------------------------------------------------------
    // Counter / characteristic ops
    // -------------------------------------------------------------------------

    private static RtValue SetCounter(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        // SetCounter(entity, counter_name, value)
        if (call.Args.Count < 3) return new RtVoid();
        var entityRef = ev.Eval(ExprOf(call.Args[0]), env);
        string counterName = IdentName(ExprOf(call.Args[1]));
        int value = Evaluator.AsInt(ev.Eval(ExprOf(call.Args[2]), env));

        if (entityRef is RtEntityRef er && ev.State.Entities.TryGetValue(er.Id, out var entity))
        {
            entity.Counters[counterName] = value;
        }
        return new RtVoid();
    }

    private static RtValue IncCounter(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 3) return new RtVoid();
        var entityRef = ev.Eval(ExprOf(call.Args[0]), env);
        string counterName = IdentName(ExprOf(call.Args[1]));
        int delta = Evaluator.AsInt(ev.Eval(ExprOf(call.Args[2]), env));

        if (entityRef is RtEntityRef er && ev.State.Entities.TryGetValue(er.Id, out var entity))
        {
            entity.Counters.TryGetValue(counterName, out var current);
            entity.Counters[counterName] = current + delta;
        }
        return new RtVoid();
    }

    private static RtValue ClearCounter(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 2) return new RtVoid();
        var entityRef = ev.Eval(ExprOf(call.Args[0]), env);
        string counterName = IdentName(ExprOf(call.Args[1]));

        if (entityRef is RtEntityRef er && ev.State.Entities.TryGetValue(er.Id, out var entity))
        {
            entity.Counters[counterName] = 0;
        }
        return new RtVoid();
    }

    private static RtValue SetCharacteristic(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 3) return new RtVoid();
        var entityRef = ev.Eval(ExprOf(call.Args[0]), env);
        string name = IdentName(ExprOf(call.Args[1]));
        var value = ev.Eval(ExprOf(call.Args[2]), env);

        if (entityRef is RtEntityRef er && ev.State.Entities.TryGetValue(er.Id, out var entity))
        {
            entity.Characteristics[name] = value;
        }
        return new RtVoid();
    }

    private static RtValue SetFlag(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        // Two forms: SetFlag(flag_ref, bool) — flag_ref is a path to a
        // state_flags slot like Arena[Left].collapsed_for[Player1]. v1 only
        // needs it as a no-op (Setup doesn't collapse conduits).
        return new RtVoid();
    }

    // -------------------------------------------------------------------------
    // Deck ops
    // -------------------------------------------------------------------------

    private static RtValue Shuffle(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count == 0) return new RtVoid();
        var target = ev.Eval(ExprOf(call.Args[0]), env);
        if (target is RtZoneRef zr && ev.State.Entities.TryGetValue(zr.OwnerId, out var owner) &&
            owner.Zones.TryGetValue(zr.Name, out var zone))
        {
            ev.Scheduler.ShuffleInPlace(zone.Contents);
        }
        return new RtVoid();
    }

    private static RtValue Draw(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        // Draw(player) or Draw(player, amount) or Draw(player, amount: N).
        if (call.Args.Count == 0) return new RtVoid();
        var playerRef = ev.Eval(ExprOf(call.Args[0]), env);
        int amount = 1;
        if (call.Args.Count >= 2)
        {
            amount = Evaluator.AsInt(ev.Eval(ExprOf(call.Args[1]), env));
        }
        foreach (var a in call.Args.OfType<AstArgNamed>())
        {
            if (a.Name == "amount") amount = Evaluator.AsInt(ev.Eval(a.Value, env));
        }

        if (playerRef is not RtEntityRef er || !ev.State.Entities.TryGetValue(er.Id, out var player))
        {
            return new RtVoid();
        }
        if (!player.Zones.TryGetValue("Arsenal", out var arsenal) ||
            !player.Zones.TryGetValue("Hand", out var hand))
        {
            return new RtVoid();
        }
        int n = Math.Min(amount, arsenal.Contents.Count);
        for (int i = 0; i < n; i++)
        {
            // Top of a sequential Arsenal = last element (push/pop semantics).
            int top = arsenal.Contents[^1];
            arsenal.Contents.RemoveAt(arsenal.Contents.Count - 1);
            hand.Contents.Add(top);
        }
        ev.Log.LogDebug("Draw: player {Player} drew {Drawn} of {Requested} requested", player.DisplayName, n, amount);
        return new RtVoid();
    }

    // -------------------------------------------------------------------------
    // RandomChoose / InstantiateEntity
    // -------------------------------------------------------------------------

    private static RtValue RandomChoose(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        // RandomChoose(value: {...}, bind: Target.path)
        AstExpr? valueExpr = null;
        AstExpr? bindExpr = null;
        foreach (var a in call.Args)
        {
            if (a is AstArgNamed { Name: "value" } n1) valueExpr = n1.Value;
            else if (a is AstArgNamed { Name: "bind" } n2) bindExpr = n2.Value;
        }
        if (valueExpr is null) return new RtVoid();

        var source = ev.Eval(valueExpr, env);
        var items = Evaluator.AsList(source);
        if (items.Count == 0) return new RtVoid();
        int idx = ev.Scheduler.NextInt(items.Count);
        var chosen = items[idx];

        if (bindExpr is AstMemberAccess ma)
        {
            var target = ev.Eval(ma.Target, env);
            if (target is RtEntityRef er && ev.State.Entities.TryGetValue(er.Id, out var entity))
            {
                entity.Characteristics[ma.Member] = chosen;
                ev.Log.LogDebug("RandomChoose: bound {Target}.{Member} = {Value}",
                    entity.DisplayName, ma.Member, chosen);
            }
        }
        return chosen;
    }

    private static RtValue InstantiateEntity(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        // Named args: kind, owner, arena, initial_counters, ...
        string? kind = null;
        var parameters = new Dictionary<string, RtValue>();
        var initialCounters = new Dictionary<string, int>();

        foreach (var a in call.Args)
        {
            if (a is AstArgNamed named)
            {
                if (named.Name == "kind")
                {
                    kind = IdentName(named.Value);
                }
                else if (named.Name == "initial_counters" && named.Value is AstBraceExpr be)
                {
                    foreach (var entry in be.Entries)
                    {
                        if (entry is AstBraceField bf)
                        {
                            initialCounters[bf.Field.Key.Name] =
                                Evaluator.AsInt(ev.Eval(ExprOfFieldValue(bf.Field.Value), env));
                        }
                    }
                }
                else
                {
                    parameters[named.Name] = ev.Eval(named.Value, env);
                }
            }
        }

        if (kind is null) return new RtVoid();
        var entity = ev.State.AllocateEntity(kind, kind);
        foreach (var (k, v) in parameters) entity.Parameters[k] = v;
        foreach (var (k, v) in initialCounters) entity.Counters[k] = v;
        if (parameters.TryGetValue("owner", out var ownerVal) && ownerVal is RtEntityRef oer)
        {
            entity.OwnerId = oer.Id;
        }
        ev.Log.LogDebug("InstantiateEntity {Kind} #{Id}", kind, entity.Id);
        return new RtEntityRef(entity.Id);
    }

    // -------------------------------------------------------------------------
    // Aether / resource ops
    // -------------------------------------------------------------------------

    private static RtValue RefillAether(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        // RefillAether(player, amount: N) — set aether to max(0, N).
        if (call.Args.Count == 0) return new RtVoid();
        var playerRef = ev.Eval(ExprOf(call.Args[0]), env);
        int amount = 0;
        foreach (var a in call.Args.OfType<AstArgNamed>())
        {
            if (a.Name == "amount") amount = Evaluator.AsInt(ev.Eval(a.Value, env));
        }
        if (playerRef is RtEntityRef er && ev.State.Entities.TryGetValue(er.Id, out var p))
        {
            p.Counters["aether"] = Math.Max(0, amount);
        }
        return new RtVoid();
    }

    private static RtValue PayAether(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 2) return new RtVoid();
        var playerRef = ev.Eval(ExprOf(call.Args[0]), env);
        int cost = Evaluator.AsInt(ev.Eval(ExprOf(call.Args[1]), env));
        if (playerRef is RtEntityRef er && ev.State.Entities.TryGetValue(er.Id, out var p))
        {
            p.Counters.TryGetValue("aether", out var cur);
            p.Counters["aether"] = Math.Max(0, cur - cost);
        }
        return new RtVoid();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static AstExpr? FirstPositional(AstFunctionCall call)
    {
        foreach (var a in call.Args)
        {
            if (a is AstArgPositional p) return p.Value;
        }
        return call.Args.Count > 0 ? ExprOf(call.Args[0]) : null;
    }

    private static RtValue EvalFirst(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        var expr = FirstPositional(call);
        return expr is null ? new RtVoid() : ev.Eval(expr, env);
    }

    private static AstExpr ExprOf(AstArg a) => a switch
    {
        AstArgPositional p => p.Value,
        AstArgNamed n => n.Value,
        AstArgBinding b => b.Value,
        _ => throw new InvalidOperationException("unreachable"),
    };

    private static AstExpr ExprOfFieldValue(AstFieldValue v) => v switch
    {
        AstFieldExpr fe => fe.Value,
        AstFieldTyped ft => ft.Value,
        AstFieldBlock fb => new AstBraceExpr(fb.Span,
            fb.Block.Fields.Select(f => (AstBraceEntry)new AstBraceField(f.Span, f)).ToList()),
        _ => throw new InvalidOperationException("unreachable field value"),
    };

    private static string IdentName(AstExpr e) => e switch
    {
        AstIdent id => id.Name,
        AstStringLit s => s.Value,
        _ => e.ToString() ?? "<expr>",
    };

    private static string Describe(AstExpr e) => e switch
    {
        AstIdent id => id.Name,
        _ => e.GetType().Name,
    };
}
