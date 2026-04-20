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
            case "BeginPhase":              result = BeginPhase(call, env, ev); return true;
            case "EnterMainPhase":          result = EnterMainPhase(call, env, ev); return true;
            case "ResolveClashPhase":       result = ResolveClashPhase(call, env, ev); return true;

            // ----- Target / Damage -----
            case "Target":                  result = Target(call, env, ev); return true;
            case "DealDamage":              result = DealDamage(call, env, ev); return true;
            case "Heal":                    result = Heal(call, env, ev); return true;
            case "HealSelfArenaConduit":    result = HealSelfArenaConduit(call, env, ev); return true;
            case "IgniteTickArenaConduit":  result = IgniteTickArenaConduit(call, env, ev); return true;

            // ----- Resonance / tier predicates -----
            case "CountEcho":               result = CountEchoBuiltin(call, env, ev); return true;
            case "Resonance":               result = ResonanceBuiltin(call, env, ev); return true;
            case "Peak":                    result = PeakBuiltin(call, env, ev); return true;
            case "Banner":                  result = BannerBuiltin(call, env, ev); return true;
            case "BannerExists":            result = BannerExistsBuiltin(call, env, ev); return true;
            case "Tiers":                   result = TiersBuiltin(call, env, ev); return true;
            case "When":                    result = WhenBuiltin(call, env, ev); return true;

            // ----- Replacement guards / helpers -----
            case "HasDuplicateInPlay":      result = HasDuplicateInPlayBuiltin(call, env, ev); return true;
            case "has_keyword":             result = HasKeywordBuiltin(call, env, ev); return true;

            // ----- Phantom / Drift support -----
            case "SetPhantoming":           result = SetPhantomingBuiltin(call, env, ev); return true;
            case "PhantomReturn":           result = PhantomReturnBuiltin(call, env, ev); return true;
            case "DriftMoveUnit":           result = DriftMoveUnitBuiltin(call, env, ev); return true;

            // ----- Sprawl / token creation -----
            case "CreateToken":             result = CreateTokenBuiltin(call, env, ev); return true;
            case "Sprawl":                  result = SprawlBuiltin(call, env, ev); return true;

            // ----- Mulligan / MoveTo -----
            case "PerformMulligan":         result = new RtVoid(); return true;
            case "MoveTo":                  result = MoveTo(call, env, ev); return true;
            case "bottom_of":               result = BottomOf(call, env, ev); return true;

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
        AstExpr? chooserExpr = null;
        string? chooserLabel = null;
        foreach (var a in call.Args)
        {
            switch (a)
            {
                case AstArgNamed { Name: "options" } n: optionsExpr = n.Value; break;
                case AstArgNamed { Name: "chooser" } n2:
                    chooserExpr = n2.Value;
                    chooserLabel = Describe(n2.Value);
                    break;
            }
        }
        if (optionsExpr is not AstBraceExpr be) return new RtVoid();

        // Chooser → PlayerId. Best-effort; if the chooser isn't an entity we
        // know about (e.g. `self.controller` during Setup, where no self is in
        // scope), PlayerId stays null and the host surfaces a prompt with no
        // seat attribution.
        int? chooserPlayerId = null;
        if (chooserExpr is not null)
        {
            var chosen = ev.Eval(chooserExpr, env);
            if (chosen is RtEntityRef er &&
                ev.State.Entities.TryGetValue(er.Id, out var entity) &&
                entity.Kind == "Player")
            {
                chooserPlayerId = er.Id;
            }
        }

        // Snapshot the option keys before asking for input — this is what
        // GetLegalActions returns while the interpreter is suspended here.
        var legal = new List<LegalAction>();
        foreach (var entry in be.Entries)
        {
            if (entry is AstBraceField bf)
            {
                legal.Add(new LegalAction("choice_option", bf.Field.Key.Name));
            }
        }

        var request = new InputRequest(
            Prompt: $"Choice({chooserLabel ?? "?"})",
            PlayerId: chooserPlayerId,
            LegalActions: legal);

        var choice = ev.Scheduler.Inputs.Next(request);
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

    // -------------------------------------------------------------------------
    // Target — pick a single entity matching a lambda predicate.
    // -------------------------------------------------------------------------

    private static RtValue Target(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        // Two argument shapes, both exercised in the real encoding:
        //   Target(lambda, effect)                         // positional
        //   Target(selector: lambda, chooser: p,
        //          bind: target, effect: expr)            // named
        // The bound variable defaults to `target`. If no effect is supplied
        // we still return the chosen entity so callers using `Target(...)`
        // inline (e.g. `let t = Target(...)`) get a usable value.
        AstExpr? selectorExpr = null;
        AstExpr? effectExpr = null;
        AstExpr? chooserExpr = null;
        string bindName = "target";
        int positional = 0;
        foreach (var a in call.Args)
        {
            switch (a)
            {
                case AstArgPositional pa:
                    if (positional == 0) selectorExpr = pa.Value;
                    else if (positional == 1) effectExpr = pa.Value;
                    positional++;
                    break;
                case AstArgNamed { Name: "selector" } n: selectorExpr = n.Value; break;
                case AstArgNamed { Name: "effect" } n: effectExpr = n.Value; break;
                case AstArgNamed { Name: "chooser" } n: chooserExpr = n.Value; break;
                case AstArgNamed { Name: "bind" } n: bindName = IdentName(n.Value); break;
            }
        }
        if (selectorExpr is null) return new RtVoid();

        var lambdaVal = ev.Eval(selectorExpr, env);
        if (lambdaVal is not RtLambda lambda || lambda.Parameters.Count != 1)
        {
            ev.Log.LogDebug("Target: selector is not a 1-arg lambda");
            return new RtVoid();
        }
        string paramName = lambda.Parameters[0];

        // Snapshot entity ids before iterating so selector side-effects
        // (there shouldn't be any, but...) can't grow the set mid-scan.
        var snapshot = ev.State.Entities.Values.ToList();
        var candidates = new List<Entity>();
        // Source controller — used for the Shroud legality check. When a
        // Maneuver's OnResolve calls Target, the env carries `controller`
        // bound to the card's controller via PlayManeuver.
        int? sourceController = null;
        if (env.TryLookup("controller", out var ctl) && ctl is RtEntityRef ctlRef)
        {
            sourceController = ctlRef.Id;
        }
        foreach (var entity in snapshot)
        {
            // Shroud: opposing effects can't choose this entity as an explicit
            // target. Same-controller self-targets and non-targeted sweepers
            // are unaffected (sweepers don't go through Target).
            if (sourceController is int sc &&
                KeywordRuntime.HasKeyword(entity, "Shroud") &&
                entity.OwnerId is int eoid && eoid != sc)
            {
                continue;
            }
            var testEnv = env.Extend(paramName, new RtEntityRef(entity.Id));
            if (Evaluator.AsBool(ev.Eval(lambda.Body, testEnv)))
            {
                candidates.Add(entity);
            }
        }
        if (candidates.Count == 0) return new RtVoid();

        int? chooserPlayerId = null;
        if (chooserExpr is not null)
        {
            var chosen = ev.Eval(chooserExpr, env);
            if (chosen is RtEntityRef cer &&
                ev.State.Entities.TryGetValue(cer.Id, out var ce) &&
                ce.Kind == "Player")
            {
                chooserPlayerId = cer.Id;
            }
        }
        // Default chooser is the card's controller, bound by PlayCard's
        // OnResolve evaluator. Without this a no-explicit-chooser Target
        // (every Maneuver in the real encoding: Spark, Smolder, Refract…)
        // surfaces on the frontend as "not your turn" and the buttons
        // disable.
        if (chooserPlayerId is null && env.TryLookup("controller", out var controllerVal)
            && controllerVal is RtEntityRef ctrlRef
            && ev.State.Entities.TryGetValue(ctrlRef.Id, out var ctrlEnt)
            && ctrlEnt.Kind == "Player")
        {
            chooserPlayerId = ctrlRef.Id;
        }

        var legal = candidates.Select(c => new LegalAction(
            Kind: "target_entity",
            Label: $"target:{c.Id}",
            Metadata: new Dictionary<string, string>
            {
                ["entityId"] = c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["kind"] = c.Kind,
                ["displayName"] = c.DisplayName,
            })).ToList();

        var request = new InputRequest(
            Prompt: $"Target({bindName})",
            PlayerId: chooserPlayerId,
            LegalActions: legal);

        var choice = ev.Scheduler.Inputs.Next(request);
        string label = choice switch
        {
            RtSymbol s => s.Name,
            RtString s => s.V,
            _ => choice.ToString() ?? "",
        };

        int targetId;
        if (label.StartsWith("target:") &&
            int.TryParse(label.AsSpan("target:".Length), out targetId) &&
            candidates.Any(c => c.Id == targetId))
        {
            // Bind and (optionally) evaluate effect.
            if (effectExpr is not null)
            {
                var effEnv = env.Extend(bindName, new RtEntityRef(targetId));
                ev.Eval(effectExpr, effEnv);
            }
            return new RtEntityRef(targetId);
        }

        ev.Log.LogDebug("Target: ignored unrecognised choice {Label}", label);
        return new RtVoid();
    }

    // -------------------------------------------------------------------------
    // HealSelfArenaConduit / IgniteTickArenaConduit — keyword-macro helpers.
    //
    // Mend and Ignite both reference `self.controller.Conduit(self.arena)`
    // via macro expansion; that path needs a MemberCall evaluator the v1
    // interpreter doesn't have. These builtins package the lookup so the
    // synthesised keyword abilities in KeywordRuntime can express
    // "heal the owner's Conduit in my arena" / "chip the opponent's Conduit
    // in my arena" without inventing a full accessor algebra.
    // -------------------------------------------------------------------------

    private static RtValue HealSelfArenaConduit(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 2) return new RtVoid();
        var selfRef = ev.Eval(ExprOf(call.Args[0]), env);
        int amount = Evaluator.AsInt(ev.Eval(ExprOf(call.Args[1]), env));
        if (selfRef is not RtEntityRef er ||
            !ev.State.Entities.TryGetValue(er.Id, out var unit) ||
            unit.OwnerId is not int ownerId ||
            !unit.Parameters.TryGetValue("arena", out var av) ||
            av is not RtSymbol arenaSym)
        {
            return new RtVoid();
        }
        var conduit = FindConduit(ev, ownerId, arenaSym.Name);
        if (conduit is null) return new RtVoid();

        int cap = GetStartingIntegrity(conduit);
        int before = conduit.Counters.GetValueOrDefault("integrity", 0);
        int after = Math.Min(cap, before + amount);
        if (after == before) return new RtVoid();
        conduit.Counters["integrity"] = after;
        ev.State.PendingEvents.Enqueue(new GameEvent("ConduitHealed",
            new Dictionary<string, RtValue>
            {
                ["source"] = new RtEntityRef(unit.Id),
                ["target"] = new RtEntityRef(conduit.Id),
                ["counter"] = new RtSymbol("integrity"),
                ["amount"] = new RtInt(after - before),
            }));
        return new RtVoid();
    }

    private static RtValue IgniteTickArenaConduit(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 2) return new RtVoid();
        var selfRef = ev.Eval(ExprOf(call.Args[0]), env);
        int amount = Evaluator.AsInt(ev.Eval(ExprOf(call.Args[1]), env));
        if (selfRef is not RtEntityRef er ||
            !ev.State.Entities.TryGetValue(er.Id, out var unit) ||
            unit.OwnerId is not int ownerId ||
            !unit.Parameters.TryGetValue("arena", out var av) ||
            av is not RtSymbol arenaSym)
        {
            return new RtVoid();
        }
        if (amount <= 0) return new RtVoid();
        var opponent = ev.State.Players.FirstOrDefault(p => p.Id != ownerId);
        if (opponent is null) return new RtVoid();

        // GameRules: Ignite N pings opposing Units with current_ramparts ≤ 2
        // in this Arena for N damage each. When no Unit qualifies, the
        // opposing Conduit takes the chip instead so Ignite has a floor
        // effect even on an empty arena.
        bool hitAnyUnit = false;
        foreach (var opposing in ev.State.Entities.Values.ToList())
        {
            if (opposing.OwnerId != opponent.Id) continue;
            if (!opposing.Characteristics.TryGetValue("in_play", out var ip) ||
                ip is not RtBool ib || !ib.V) continue;
            if (!opposing.Parameters.TryGetValue("arena", out var aa) ||
                aa is not RtSymbol ap || ap.Name != arenaSym.Name) continue;
            int ramparts = opposing.Counters.GetValueOrDefault("current_ramparts", 0);
            if (ramparts > 2) continue;
            int before = opposing.Counters.GetValueOrDefault("current_ramparts", 0);
            opposing.Counters["current_ramparts"] = Math.Max(0, before - amount);
            hitAnyUnit = true;
            ev.State.PendingEvents.Enqueue(new GameEvent("DamageDealt",
                new Dictionary<string, RtValue>
                {
                    ["source"] = new RtEntityRef(unit.Id),
                    ["target"] = new RtEntityRef(opposing.Id),
                    ["counter"] = new RtSymbol("current_ramparts"),
                    ["amount"] = new RtInt(amount),
                    ["reason"] = new RtSymbol("Ignite"),
                }));
        }

        if (!hitAnyUnit)
        {
            var conduit = FindConduit(ev, opponent.Id, arenaSym.Name);
            if (conduit is null) return new RtVoid();
            int before = conduit.Counters.GetValueOrDefault("integrity", 0);
            conduit.Counters["integrity"] = Math.Max(0, before - amount);
            ev.State.PendingEvents.Enqueue(new GameEvent("DamageDealt",
                new Dictionary<string, RtValue>
                {
                    ["source"] = new RtEntityRef(unit.Id),
                    ["target"] = new RtEntityRef(conduit.Id),
                    ["counter"] = new RtSymbol("integrity"),
                    ["amount"] = new RtInt(amount),
                    ["reason"] = new RtSymbol("Ignite"),
                }));
        }
        return new RtVoid();
    }

    // -------------------------------------------------------------------------
    // MoveTo / bottom_of — zone-moving helpers used by keyword macros that
    // relocate entities (Recur, Pilfer's exile, Phantom's return-to-hand,
    // Thessa's take-from-Cache).
    // -------------------------------------------------------------------------

    private static RtValue MoveTo(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 2) return new RtVoid();
        var entityVal = ev.Eval(ExprOf(call.Args[0]), env);
        var zoneVal = ev.Eval(ExprOf(call.Args[1]), env);
        if (entityVal is not RtEntityRef er ||
            !ev.State.Entities.TryGetValue(er.Id, out var entity))
        {
            return new RtVoid();
        }

        // Resolve destination zone. `zoneVal` may already be an
        // RtZoneRef (bare `owner.Cache`), a tagged RtZoneRef produced by
        // `bottom_of(...)`, or an RtEntityRef (treat as "move into this
        // entity's default zone", not supported in v1).
        bool appendAtBottom = false;
        RtZoneRef? dest = null;
        switch (zoneVal)
        {
            case RtZoneRef zr: dest = zr; break;
            case RtTuple t when t.Elements.Count == 2
                               && t.Elements[0] is RtSymbol { Name: "bottom" }
                               && t.Elements[1] is RtZoneRef zrb:
                dest = zrb; appendAtBottom = true; break;
        }
        if (dest is null) return new RtVoid();

        // Remove from wherever it currently is.
        foreach (var holder in ev.State.Entities.Values)
        {
            foreach (var z in holder.Zones.Values) z.Contents.Remove(er.Id);
        }
        if (ev.State.Entities.TryGetValue(dest.OwnerId, out var owner) &&
            owner.Zones.TryGetValue(dest.Name, out var zone))
        {
            if (appendAtBottom)
            {
                zone.Contents.Insert(0, er.Id);
            }
            else
            {
                zone.Contents.Add(er.Id);
            }
        }

        // Unit-specific: leaving Battlefield clears in_play.
        if (dest.Name != "Battlefield")
        {
            entity.Characteristics["in_play"] = new RtBool(false);
        }
        ev.State.PendingEvents.Enqueue(new GameEvent("ZoneMoved",
            new Dictionary<string, RtValue>
            {
                ["entity"] = new RtEntityRef(entity.Id),
                ["zone"] = new RtSymbol(dest.Name),
                ["owner"] = new RtEntityRef(dest.OwnerId),
            }));
        return new RtVoid();
    }

    private static RtValue BottomOf(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 1) return new RtVoid();
        var zv = ev.Eval(ExprOf(call.Args[0]), env);
        if (zv is RtZoneRef zr)
        {
            return new RtTuple(new List<RtValue> { new RtSymbol("bottom"), zr });
        }
        return zv;
    }

    // -------------------------------------------------------------------------
    // Heal — additive inverse of DealDamage. Restores the first available
    // counter (current_ramparts / current_hp / integrity) up to its starting
    // value. Used by Reconstitute, SealTheBreach, PatchJob, etc.
    // -------------------------------------------------------------------------

    private static RtValue Heal(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 2) return new RtVoid();
        var targetRef = ev.Eval(ExprOf(call.Args[0]), env);
        int amount = Evaluator.AsInt(ev.Eval(ExprOf(call.Args[1]), env));
        if (targetRef is not RtEntityRef er ||
            !ev.State.Entities.TryGetValue(er.Id, out var entity))
        {
            return new RtVoid();
        }

        int cap = int.MaxValue;
        foreach (var a in call.Args)
        {
            if (a is AstArgNamed n && n.Name == "cap")
            {
                var capVal = ev.Eval(n.Value, env);
                if (capVal is RtInt ci) cap = ci.V;
                else if (capVal is RtSymbol cs && cs.Name == "starting_integrity")
                    cap = GetStartingIntegrity(entity);
            }
        }

        foreach (var counter in new[] { "integrity", "current_hp", "current_ramparts" })
        {
            if (!entity.Counters.ContainsKey(counter)) continue;
            int before = entity.Counters[counter];
            int afterCap = counter == "integrity"
                ? Math.Min(cap, GetStartingIntegrity(entity))
                : cap;
            int after = Math.Min(afterCap, before + amount);
            if (after == before) return new RtVoid();
            entity.Counters[counter] = after;
            ev.State.PendingEvents.Enqueue(new GameEvent("Healed",
                new Dictionary<string, RtValue>
                {
                    ["target"] = new RtEntityRef(entity.Id),
                    ["counter"] = new RtSymbol(counter),
                    ["amount"] = new RtInt(after - before),
                }));
            return new RtVoid();
        }
        return new RtVoid();
    }

    // -------------------------------------------------------------------------
    // Resonance-field predicates — count pushed echoes against the current
    // controller's field. `controller` is bound by DispatchEvent / OnResolve;
    // falls back to the first player when the env doesn't carry one (e.g.,
    // Game-level triggered abilities with no owning Unit).
    // -------------------------------------------------------------------------

    private static Entity? ResolveController(RtEnv env, Evaluator ev)
    {
        if (env.TryLookup("controller", out var v) && v is RtEntityRef er &&
            ev.State.Entities.TryGetValue(er.Id, out var p) && p.Kind == "Player")
        {
            return p;
        }
        return ev.State.Players.FirstOrDefault();
    }

    private static RtValue CountEchoBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 1) return new RtInt(0);
        var factionVal = ev.Eval(ExprOf(call.Args[0]), env);
        if (factionVal is not RtSymbol factionSym) return new RtInt(0);
        var controller = ResolveController(env, ev);
        if (controller is null) return new RtInt(0);
        return new RtInt(KeywordRuntime.CountEcho(controller, factionSym.Name));
    }

    private static RtValue ResonanceBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 2) return new RtBool(false);
        var factionVal = ev.Eval(ExprOf(call.Args[0]), env);
        int n = Evaluator.AsInt(ev.Eval(ExprOf(call.Args[1]), env));
        if (factionVal is not RtSymbol factionSym) return new RtBool(false);
        var controller = ResolveController(env, ev);
        if (controller is null) return new RtBool(false);
        return new RtBool(KeywordRuntime.CountEcho(controller, factionSym.Name) >= n);
    }

    private static RtValue PeakBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 1) return new RtBool(false);
        var factionVal = ev.Eval(ExprOf(call.Args[0]), env);
        if (factionVal is not RtSymbol factionSym) return new RtBool(false);
        var controller = ResolveController(env, ev);
        if (controller is null) return new RtBool(false);
        return new RtBool(
            KeywordRuntime.CountEcho(controller, factionSym.Name) >=
            KeywordRuntime.ResonanceFieldCapacity);
    }

    private static RtValue BannerBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 1) return new RtBool(false);
        var factionVal = ev.Eval(ExprOf(call.Args[0]), env);
        if (factionVal is not RtSymbol factionSym) return new RtBool(false);
        var controller = ResolveController(env, ev);
        if (controller is null) return new RtBool(false);
        // Banner(F) = "F is the most-pushed faction on your field".
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var echo in KeywordRuntime.GetResonanceField(controller))
        {
            if (echo is not RtSymbol s) continue;
            counts[s.Name] = counts.GetValueOrDefault(s.Name, 0) + 1;
        }
        if (counts.Count == 0) return new RtBool(false);
        int max = counts.Values.Max();
        if (counts.GetValueOrDefault(factionSym.Name, 0) != max) return new RtBool(false);
        // Tie-break: only a single faction at max holds the Banner.
        int tiesAtMax = counts.Count(kv => kv.Value == max);
        return new RtBool(tiesAtMax == 1);
    }

    private static RtValue BannerExistsBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        _ = call;
        var controller = ResolveController(env, ev);
        if (controller is null) return new RtBool(false);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var echo in KeywordRuntime.GetResonanceField(controller))
        {
            if (echo is not RtSymbol s) continue;
            counts[s.Name] = counts.GetValueOrDefault(s.Name, 0) + 1;
        }
        if (counts.Count == 0) return new RtBool(false);
        int max = counts.Values.Max();
        return new RtBool(counts.Count(kv => kv.Value == max) == 1);
    }

    /// <summary>
    /// Tiers([(cond_1, effect_1), ...]) — evaluates each case in order,
    /// runs the first whose predicate is true. The last case is typically
    /// <c>Default</c>, which always fires. Matches the macro's intent in
    /// <c>encoding/engine/01-resonance-macros.ccgnf</c>.
    /// </summary>
    private static RtValue TiersBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        var arg = FirstPositional(call);
        if (arg is not AstListLit list) return new RtVoid();
        foreach (var el in list.Elements)
        {
            if (el is not AstParen paren || paren.Elements.Count != 2) continue;
            var condExpr = paren.Elements[0];
            var effectExpr = paren.Elements[1];

            bool fires;
            if (condExpr is AstIdent id && id.Name == "Default")
            {
                fires = true;
            }
            else
            {
                var v = ev.Eval(condExpr, env);
                fires = v is RtBool rb && rb.V;
            }
            if (fires)
            {
                ev.Eval(effectExpr, env);
                return new RtVoid();
            }
        }
        return new RtVoid();
    }

    /// <summary>
    /// When(cond, effect) — Tiers's single-case shorthand. `Tiers([(cond,
    /// effect)])` with no Default fall-through.
    /// </summary>
    private static RtValue WhenBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 2) return new RtVoid();
        var cond = ev.Eval(ExprOf(call.Args[0]), env);
        if (cond is RtBool b && b.V)
        {
            ev.Eval(ExprOf(call.Args[1]), env);
        }
        return new RtVoid();
    }

    /// <summary>
    /// HasDuplicateInPlay(self) — true when the controller already has
    /// another card with the same name in-play (Unique replacement guard).
    /// </summary>
    private static RtValue HasDuplicateInPlayBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 1) return new RtBool(false);
        var v = ev.Eval(ExprOf(call.Args[0]), env);
        if (v is not RtEntityRef er ||
            !ev.State.Entities.TryGetValue(er.Id, out var self))
        {
            return new RtBool(false);
        }
        foreach (var other in ev.State.Entities.Values)
        {
            if (other.Id == self.Id) continue;
            if (other.OwnerId != self.OwnerId) continue;
            if (!string.Equals(other.DisplayName, self.DisplayName, StringComparison.Ordinal)) continue;
            if (!other.Characteristics.TryGetValue("in_play", out var ip) ||
                ip is not RtBool ib || !ib.V) continue;
            return new RtBool(true);
        }
        return new RtBool(false);
    }

    // -------------------------------------------------------------------------
    // Phantom helpers.
    //
    // v1 wiring is auto-fade: Phantom Units always fade at Start of Clash
    // and always return to hand at End of Clash. The authored Choice is
    // conceptually preserved for a later wave — interactive clients should
    // prompt the controller, but the bench bots don't score Phantom yet and
    // the auto-fade shape is the simplest way to prove the pipeline
    // (PhaseBegin Clash → set phantoming tag → zero projected force /
    // fortification → PhaseEnd Clash → return to hand → reduce base cost
    // → emit PhantomReturn event for the raw-Triggered cards that listen
    // for it).
    // -------------------------------------------------------------------------

    private static RtValue SetPhantomingBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 1) return new RtVoid();
        var v = ev.Eval(ExprOf(call.Args[0]), env);
        if (v is not RtEntityRef er ||
            !ev.State.Entities.TryGetValue(er.Id, out var unit))
        {
            return new RtVoid();
        }
        bool on = true;
        if (call.Args.Count >= 2)
        {
            var flag = ev.Eval(ExprOf(call.Args[1]), env);
            on = flag is RtBool rb ? rb.V : true;
        }
        if (on) unit.Tags.Add("phantoming");
        else    unit.Tags.Remove("phantoming");
        return new RtVoid();
    }

    private static RtValue PhantomReturnBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 1) return new RtVoid();
        var v = ev.Eval(ExprOf(call.Args[0]), env);
        if (v is not RtEntityRef er ||
            !ev.State.Entities.TryGetValue(er.Id, out var unit) ||
            unit.OwnerId is not int ownerId ||
            !ev.State.Entities.TryGetValue(ownerId, out var owner))
        {
            return new RtVoid();
        }
        // Only fires when the unit is actually phantoming this clash.
        if (!unit.Tags.Contains("phantoming")) return new RtVoid();

        // Remove from every zone, then drop into Hand.
        foreach (var h in ev.State.Entities.Values)
        {
            foreach (var z in h.Zones.Values) z.Contents.Remove(unit.Id);
        }
        if (owner.Zones.TryGetValue("Hand", out var hand)) hand.Contents.Add(unit.Id);
        unit.Characteristics["in_play"] = new RtBool(false);
        unit.Tags.Remove("phantoming");

        // Persistent base-cost reduction (min 0). Stored on the card
        // entity; the play-protocol reads this when computing the next
        // play's cost.
        int reduction = unit.Counters.GetValueOrDefault("base_cost_reduction", 0) + 1;
        unit.Counters["base_cost_reduction"] = reduction;

        ev.State.PendingEvents.Enqueue(new GameEvent("PhantomReturn",
            new Dictionary<string, RtValue>
            {
                ["target"] = new RtEntityRef(unit.Id),
                ["reduction"] = new RtInt(reduction),
            }));
        return new RtVoid();
    }

    // -------------------------------------------------------------------------
    // Drift — move unit to an adjacent non-collapsed arena at end of its
    // controller's turn. v1 simplification: pick the first adjacent arena
    // whose conduit (for the owner) is still alive; no player Choice.
    // -------------------------------------------------------------------------

    private static RtValue DriftMoveUnitBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 1) return new RtVoid();
        var v = ev.Eval(ExprOf(call.Args[0]), env);
        if (v is not RtEntityRef er ||
            !ev.State.Entities.TryGetValue(er.Id, out var unit) ||
            unit.OwnerId is not int ownerId ||
            !unit.Parameters.TryGetValue("arena", out var av) ||
            av is not RtSymbol currentArena)
        {
            return new RtVoid();
        }

        var adjacent = AdjacentArenas(currentArena.Name);
        foreach (var neighbour in adjacent)
        {
            // Skip arenas whose owner-side conduit is collapsed.
            var conduit = FindConduit(ev, ownerId, neighbour);
            if (conduit is null) continue;
            // Move.
            unit.Parameters["arena"] = new RtSymbol(neighbour);
            var targetArena = ev.State.Arenas.FirstOrDefault(a =>
                a.Parameters.TryGetValue("pos", out var ap) &&
                ap is RtSymbol aps && aps.Name == neighbour);
            if (targetArena is not null)
            {
                unit.Parameters["arena_entity"] = new RtEntityRef(targetArena.Id);
            }
            ev.State.PendingEvents.Enqueue(new GameEvent("Drifted",
                new Dictionary<string, RtValue>
                {
                    ["target"] = new RtEntityRef(unit.Id),
                    ["from"] = new RtSymbol(currentArena.Name),
                    ["to"] = new RtSymbol(neighbour),
                }));
            return new RtVoid();
        }
        return new RtVoid();
    }

    private static IEnumerable<string> AdjacentArenas(string pos) => pos switch
    {
        "Left"   => new[] { "Center", "Right" },
        "Center" => new[] { "Left",   "Right" },
        "Right"  => new[] { "Center", "Left" },
        _ => Array.Empty<string>(),
    };

    // -------------------------------------------------------------------------
    // CreateToken / Sprawl — token generation. Tokens are allocated as
    // Unit-kind entities with default stats (Force 1, Ramparts 1) and
    // placed on the controller's Battlefield in the target arena.
    // Sprawl(N) is the keyword-body form; cards like SprawlVanguard invoke
    // it directly from their OnResolve.
    // -------------------------------------------------------------------------

    private static RtValue CreateTokenBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        string template = "ThornSapling";
        RtValue? ownerRef = null;
        RtValue? arenaRef = null;
        RtValue? controllerRef = null;
        foreach (var a in call.Args)
        {
            switch (a)
            {
                case AstArgNamed { Name: "template" } n:
                    if (ev.Eval(n.Value, env) is RtSymbol ts) template = ts.Name;
                    break;
                case AstArgNamed { Name: "owner" } n2:       ownerRef = ev.Eval(n2.Value, env); break;
                case AstArgNamed { Name: "controller" } n3:  controllerRef = ev.Eval(n3.Value, env); break;
                case AstArgNamed { Name: "zone" } n4:
                case AstArgNamed { Name: "arena" } n5:
                    arenaRef = ev.Eval(a is AstArgNamed nn ? nn.Value : ExprOf(a), env);
                    break;
            }
        }
        int? ownerId = (ownerRef ?? controllerRef) is RtEntityRef er ? er.Id : null;
        if (ownerId is null) return new RtVoid();

        RtSymbol? arenaSym = arenaRef switch
        {
            RtSymbol s => s,
            RtEntityRef aer when ev.State.Entities.TryGetValue(aer.Id, out var ae)
                             && ae.Parameters.TryGetValue("pos", out var p)
                             && p is RtSymbol ps => ps,
            _ => null,
        };

        var token = ev.State.AllocateEntity("Card", template);
        token.OwnerId = ownerId;
        token.Characteristics["type"] = new RtSymbol("Unit");
        token.Characteristics["in_play"] = new RtBool(true);
        token.Characteristics["is_token"] = new RtBool(true);
        token.Counters["force"] = 1;
        token.Counters["max_ramparts"] = 1;
        token.Counters["current_ramparts"] = 1;
        if (arenaSym is not null) token.Parameters["arena"] = arenaSym;

        if (ev.State.Entities.TryGetValue(ownerId.Value, out var ownerEntity) &&
            ownerEntity.Zones.TryGetValue("Battlefield", out var bf))
        {
            bf.Contents.Add(token.Id);
        }

        ev.State.PendingEvents.Enqueue(new GameEvent("TokenCreated",
            new Dictionary<string, RtValue>
            {
                ["token"] = new RtEntityRef(token.Id),
                ["owner"] = new RtEntityRef(ownerId.Value),
            }));
        // EnterPlay so OnEnter / OnArenaEnter triggers (Rally) react to the
        // token as a real Unit.
        var enterFields = new Dictionary<string, RtValue>
        {
            ["target"] = new RtEntityRef(token.Id),
            ["player"] = new RtEntityRef(ownerId.Value),
        };
        if (arenaSym is not null) enterFields["arena"] = arenaSym;
        ev.State.PendingEvents.Enqueue(new GameEvent("EnterPlay", enterFields));
        return new RtEntityRef(token.Id);
    }

    private static RtValue SprawlBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        int count = 1;
        if (call.Args.Count > 0)
        {
            count = Evaluator.AsInt(ev.Eval(ExprOf(call.Args[0]), env));
        }
        int? ownerId = null;
        RtSymbol? arenaSym = null;
        foreach (var a in call.Args.OfType<AstArgNamed>())
        {
            if (a.Name == "in" || a.Name == "arena")
            {
                var av = ev.Eval(a.Value, env);
                arenaSym = av as RtSymbol;
            }
            else if (a.Name == "for" || a.Name == "controller")
            {
                var cv = ev.Eval(a.Value, env);
                if (cv is RtEntityRef er) ownerId = er.Id;
            }
        }
        if (ownerId is null && env.TryLookup("controller", out var cl) && cl is RtEntityRef cler)
        {
            ownerId = cler.Id;
        }
        if (ownerId is null) return new RtVoid();
        if (arenaSym is null && env.TryLookup("self", out var sv) && sv is RtEntityRef ser
            && ev.State.Entities.TryGetValue(ser.Id, out var se)
            && se.Parameters.TryGetValue("arena", out var sa) && sa is RtSymbol sas)
        {
            arenaSym = sas;
        }
        for (int i = 0; i < count; i++)
        {
            var synth = new List<AstArg>
            {
                new AstArgNamed(Ccgnf.Diagnostics.SourceSpan.Unknown, "template",
                    new AstIdent(Ccgnf.Diagnostics.SourceSpan.Unknown, "ThornSapling")),
                new AstArgNamed(Ccgnf.Diagnostics.SourceSpan.Unknown, "owner",
                    new AstIdent(Ccgnf.Diagnostics.SourceSpan.Unknown, "__synthOwner__")),
            };
            if (arenaSym is not null)
            {
                synth.Add(new AstArgNamed(Ccgnf.Diagnostics.SourceSpan.Unknown, "arena",
                    new AstIdent(Ccgnf.Diagnostics.SourceSpan.Unknown, arenaSym.Name)));
            }
            var subEnv = env.Extend("__synthOwner__", new RtEntityRef(ownerId.Value));
            var subCall = new AstFunctionCall(
                Ccgnf.Diagnostics.SourceSpan.Unknown,
                new AstIdent(Ccgnf.Diagnostics.SourceSpan.Unknown, "CreateToken"),
                synth);
            CreateTokenBuiltin(subCall, subEnv, ev);
        }
        return new RtVoid();
    }

    /// <summary>
    /// has_keyword(entity, Keyword) — true if the entity carries the keyword
    /// in its tag set (see KeywordRuntime.ApplyKeywords). Used by Phantom,
    /// Shroud guards, and bot scoring.
    /// </summary>
    private static RtValue HasKeywordBuiltin(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 2) return new RtBool(false);
        var entRef = ev.Eval(ExprOf(call.Args[0]), env);
        var kw = ev.Eval(ExprOf(call.Args[1]), env);
        if (entRef is not RtEntityRef er ||
            !ev.State.Entities.TryGetValue(er.Id, out var entity))
        {
            return new RtBool(false);
        }
        string? name = kw switch
        {
            RtSymbol s => s.Name,
            RtString str => str.V,
            _ => null,
        };
        if (name is null) return new RtBool(false);
        return new RtBool(KeywordRuntime.HasKeyword(entity, name));
    }

    private static int GetStartingIntegrity(Entity conduit)
    {
        // Conduits aren't created with a permanent "starting_integrity"
        // counter, so we use the owner's Player.characteristics if we can
        // find one — falling back to 7 (the v1 default).
        if (conduit.Characteristics.TryGetValue("starting_integrity", out var v) &&
            v is RtInt ri)
        {
            return ri.V;
        }
        return 7;
    }

    // -------------------------------------------------------------------------
    // DealDamage — apply HP loss to a target entity.
    // -------------------------------------------------------------------------

    private static RtValue DealDamage(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        if (call.Args.Count < 2) return new RtVoid();
        var targetRef = ev.Eval(ExprOf(call.Args[0]), env);
        int amount = Evaluator.AsInt(ev.Eval(ExprOf(call.Args[1]), env));
        if (targetRef is not RtEntityRef er ||
            !ev.State.Entities.TryGetValue(er.Id, out var entity))
        {
            return new RtVoid();
        }

        // v1 damage order (8d slice): current_ramparts → current_hp → integrity.
        // Real §7.3 order with Ramparts / Force absorption lands in 8e.
        foreach (var counter in new[] { "current_ramparts", "current_hp", "integrity" })
        {
            if (!entity.Counters.ContainsKey(counter)) continue;
            entity.Counters[counter] = Math.Max(0, entity.Counters[counter] - amount);
            ev.State.PendingEvents.Enqueue(new GameEvent("DamageDealt",
                new Dictionary<string, RtValue>
                {
                    ["target"] = new RtEntityRef(entity.Id),
                    ["counter"] = new RtSymbol(counter),
                    ["amount"] = new RtInt(amount),
                }));
            return new RtVoid();
        }
        ev.Log.LogDebug("DealDamage: target {Id} has no damage-absorbing counter", entity.Id);
        return new RtVoid();
    }

    // -------------------------------------------------------------------------
    // Main / Channel phase — v1 priority decision.
    // -------------------------------------------------------------------------

    private static RtValue EnterMainPhase(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        // EnterMainPhase(player) — single-shot decision: the active player
        // either passes priority or plays one card from hand. v1 scope (8c);
        // multi-card-per-turn priority windows land alongside Interrupts.
        if (call.Args.Count == 0) return new RtVoid();
        var playerRef = ev.Eval(ExprOf(call.Args[0]), env);
        if (playerRef is not RtEntityRef er ||
            !ev.State.Entities.TryGetValue(er.Id, out var player))
        {
            return new RtVoid();
        }

        int aether = player.Counters.GetValueOrDefault("aether", 0);

        // Enumerate cards in hand the player can afford.
        var playable = new Dictionary<int, (Ast.AstCardDecl decl, int cost)>();
        var legal = new List<LegalAction> { new("pass_priority", "pass") };

        if (player.Zones.TryGetValue("Hand", out var hand))
        {
            foreach (var cardId in hand.Contents)
            {
                if (!ev.State.Entities.TryGetValue(cardId, out var card)) continue;
                if (!ev.State.CardDecls.TryGetValue(card.DisplayName, out var decl)) continue;
                int cost = ComputeEffectiveCost(ev, player, card, decl);
                if (cost > aether) continue;
                playable[cardId] = (decl, cost);
                legal.Add(new LegalAction(
                    Kind: "play_card",
                    Label: $"play:{cardId}",
                    Metadata: new Dictionary<string, string>
                    {
                        ["entityId"] = cardId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["cardName"] = card.DisplayName,
                        ["cost"] = cost.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["type"] = GetCardType(decl) ?? "",
                    }));
            }
        }

        var request = new InputRequest(
            Prompt: $"MainPhase({player.DisplayName})",
            PlayerId: player.Id,
            LegalActions: legal);

        var choice = ev.Scheduler.Inputs.Next(request);
        string label = choice switch
        {
            RtSymbol s => s.Name,
            RtString s => s.V,
            _ => choice.ToString() ?? "",
        };

        if (label == "pass") return new RtVoid();

        if (label.StartsWith("play:") &&
            int.TryParse(label.AsSpan("play:".Length), out var playId) &&
            playable.TryGetValue(playId, out var info))
        {
            PlayCard(ev, player, ev.State.Entities[playId], info.decl, info.cost);
        }
        else
        {
            ev.Log.LogDebug("MainPhase: ignored unrecognised choice {Label}", label);
        }
        return new RtVoid();
    }

    private static void PlayCard(
        Evaluator ev,
        Entity player,
        Entity cardEntity,
        Ast.AstCardDecl decl,
        int cost)
    {
        player.Counters["aether"] = Math.Max(0, player.Counters.GetValueOrDefault("aether", 0) - cost);

        // Push this card's factions onto the controller's ResonanceField
        // before any card-resolution effects fire, so tier predicates
        // (Resonance(F, N) / Peak(F)) evaluate against the just-pushed
        // echo — the card "contributes its own echo" per GameRules §6.3.
        KeywordRuntime.PushEchoes(player, KeywordRuntime.ReadFactions(decl));

        var type = GetCardType(decl);
        if (type == "Unit")
        {
            PlayUnit(ev, player, cardEntity, decl, cost);
        }
        else
        {
            PlayManeuver(ev, player, cardEntity, decl, cost);
        }
    }

    private static void PlayManeuver(
        Evaluator ev,
        Entity player,
        Entity cardEntity,
        Ast.AstCardDecl decl,
        int cost)
    {
        CastLog.RecordCast(cardEntity, player, decl, cost, "Maneuver", arenaPos: null);

        // Move Hand → Cache before resolving so the effect sees the card in
        // Cache (same as the real §6.2 resolution order once Interrupts land).
        if (player.Zones.TryGetValue("Hand", out var hand)) hand.Contents.Remove(cardEntity.Id);
        if (player.Zones.TryGetValue("Cache", out var cache)) cache.Contents.Add(cardEntity.Id);

        // OnResolve effect evaluates with `self` = card, `controller` = player.
        var onResolve = GetCardOnResolve(decl);
        if (onResolve is not null)
        {
            var resolveEnv = RtEnv.Empty
                .Extend("self", new RtEntityRef(cardEntity.Id))
                .Extend("controller", new RtEntityRef(player.Id));
            ev.Eval(onResolve, resolveEnv);
        }

        ev.Log.LogInformation("Played Maneuver {Name} (#{Id}) for {Cost} aether",
            cardEntity.DisplayName, cardEntity.Id, cost);

        ev.State.PendingEvents.Enqueue(new GameEvent("CardPlayed",
            new Dictionary<string, RtValue>
            {
                ["player"] = new RtEntityRef(player.Id),
                ["card"] = new RtEntityRef(cardEntity.Id),
                ["cost"] = new RtInt(cost),
                ["type"] = new RtSymbol("Maneuver"),
            }));
    }

    private static void PlayUnit(
        Evaluator ev,
        Entity player,
        Entity cardEntity,
        Ast.AstCardDecl decl,
        int cost)
    {
        // Unit play opens a target-arena pending. The Unit lives on the
        // controller's Battlefield zone and carries the chosen arena as a
        // symbol parameter so Clash can filter per-arena units later.
        var arenas = ev.State.Arenas;
        if (arenas.Count == 0)
        {
            // No Arenas defined — put the unit on the battlefield with an
            // unspecified arena. Clash with no arenas is a no-op later.
            PlaceUnit(ev, player, cardEntity, decl, cost, arenaEntityId: null, arenaPos: null);
            return;
        }

        var legal = arenas.Select(a => new LegalAction(
            Kind: "target_arena",
            Label: $"arena:{a.Id}",
            Metadata: new Dictionary<string, string>
            {
                ["entityId"] = a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["kind"] = a.Kind,
                ["displayName"] = a.DisplayName,
                ["pos"] = (a.Parameters.TryGetValue("pos", out var p) && p is RtSymbol sp) ? sp.Name : "",
            })).ToList();

        var request = new InputRequest(
            Prompt: $"PickArena({cardEntity.DisplayName})",
            PlayerId: player.Id,
            LegalActions: legal);

        var choice = ev.Scheduler.Inputs.Next(request);
        string label = choice switch
        {
            RtSymbol s => s.Name,
            RtString s => s.V,
            _ => choice.ToString() ?? "",
        };

        Entity? arena = null;
        if (label.StartsWith("arena:") && int.TryParse(label.AsSpan("arena:".Length), out var aid))
        {
            arena = arenas.FirstOrDefault(a => a.Id == aid);
        }
        if (arena is null)
        {
            ev.Log.LogDebug("PlayUnit: no arena picked for {Name}; fallback to first", cardEntity.DisplayName);
            arena = arenas[0];
        }

        RtSymbol? posSym = null;
        if (arena.Parameters.TryGetValue("pos", out var posVal) && posVal is RtSymbol ps) posSym = ps;
        PlaceUnit(ev, player, cardEntity, decl, cost, arenaEntityId: arena.Id, arenaPos: posSym);
    }

    private static void PlaceUnit(
        Evaluator ev,
        Entity player,
        Entity cardEntity,
        Ast.AstCardDecl decl,
        int cost,
        int? arenaEntityId,
        RtSymbol? arenaPos)
    {
        CastLog.RecordCast(cardEntity, player, decl, cost, "Unit", arenaPos);

        // Move Hand → Battlefield (falls back to Cache if fixture didn't
        // declare a Battlefield zone).
        if (player.Zones.TryGetValue("Hand", out var hand)) hand.Contents.Remove(cardEntity.Id);
        Zone? dest = player.Zones.TryGetValue("Battlefield", out var bf) ? bf
                   : player.Zones.TryGetValue("Cache", out var cache) ? cache : null;
        dest?.Contents.Add(cardEntity.Id);

        // Copy Unit stats from the declaration onto the runtime entity.
        cardEntity.Counters["force"] = GetCardIntField(decl, "force");
        cardEntity.Counters["max_ramparts"] = GetCardIntField(decl, "ramparts");
        cardEntity.Counters["current_ramparts"] = cardEntity.Counters["max_ramparts"];
        cardEntity.OwnerId = player.Id;
        cardEntity.Characteristics["type"] = new RtSymbol("Unit");
        cardEntity.Characteristics["in_play"] = new RtBool(true);
        if (arenaEntityId is int aid) cardEntity.Parameters["arena_entity"] = new RtEntityRef(aid);
        if (arenaPos is not null) cardEntity.Parameters["arena"] = arenaPos;

        // Keywords declared on the card (Sentinel, Fortify(N), etc.) get
        // stamped onto the runtime Unit so KeywordRuntime can consult them
        // during Clash without re-walking the AST.
        KeywordRuntime.ApplyKeywords(cardEntity, KeywordRuntime.ReadKeywords(decl));

        // Triggered / Static abilities declared on the card (OnEnter,
        // EndOfClash, OnCardPlayed, etc. — after the trigger-shorthand
        // macros expand to Triggered(...)) get attached to the runtime Unit
        // so DispatchEvent walks them. v1 only acts on Triggered in this
        // pass; Static still runs via KeywordRuntime's direct helpers.
        AttachCardAbilities(cardEntity, decl);

        // OnResolve, if any, fires after the Unit enters play. Most real
        // Units have no OnResolve (enter-the-battlefield triggers are a
        // different phase of §6.2), but the hook is free.
        var onResolve = GetCardOnResolve(decl);
        if (onResolve is not null)
        {
            var resolveEnv = RtEnv.Empty
                .Extend("self", new RtEntityRef(cardEntity.Id))
                .Extend("controller", new RtEntityRef(player.Id));
            ev.Eval(onResolve, resolveEnv);
        }

        ev.Log.LogInformation("Played Unit {Name} (#{Id}) into arena {Arena} for {Cost} aether",
            cardEntity.DisplayName, cardEntity.Id, arenaPos?.Name ?? "<none>", cost);

        // Unique replacement: if another copy of this Unit is already in
        // play for this controller, bounce this copy straight to Cache and
        // skip the EnterPlay / UnitEntered events so downstream triggers
        // don't fire for a non-existent board presence.
        if (ApplyEnterPlayReplacement(cardEntity, player, ev))
        {
            ev.State.PendingEvents.Enqueue(new GameEvent("CardPlayed",
                new Dictionary<string, RtValue>
                {
                    ["player"] = new RtEntityRef(player.Id),
                    ["card"] = new RtEntityRef(cardEntity.Id),
                    ["cost"] = new RtInt(cost),
                    ["type"] = new RtSymbol("Unit"),
                }));
            return;
        }

        var fields = new Dictionary<string, RtValue>
        {
            ["player"] = new RtEntityRef(player.Id),
            ["card"] = new RtEntityRef(cardEntity.Id),
            ["cost"] = new RtInt(cost),
            ["type"] = new RtSymbol("Unit"),
        };
        if (arenaEntityId is int aid2) fields["arena"] = new RtEntityRef(aid2);
        ev.State.PendingEvents.Enqueue(new GameEvent("UnitEntered", fields));
        ev.State.PendingEvents.Enqueue(new GameEvent("CardPlayed", fields));
        // EnterPlay is the trigger that every OnEnter / OnArenaEnter macro
        // in encoding/engine/02-trigger-shorthands.ccgnf expands to match.
        // Carry the Unit as `target` so `Event.EnterPlay(target=self)`
        // patterns on that same Unit (and on neighbours in the same arena
        // for OnArenaEnter) fire correctly.
        var enterFields = new Dictionary<string, RtValue>
        {
            ["target"] = new RtEntityRef(cardEntity.Id),
            ["player"] = new RtEntityRef(player.Id),
        };
        // Pattern matchers expect `arena` as a symbol (so `arena=self.arena`
        // comparisons resolve to a symbol-equality check). The entity ref
        // is still available via `arena_entity` for effects that need to
        // reach the Arena's abilities / characteristics.
        if (arenaPos is not null) enterFields["arena"] = arenaPos;
        if (arenaEntityId is int aid3) enterFields["arena_entity"] = new RtEntityRef(aid3);
        ev.State.PendingEvents.Enqueue(new GameEvent("EnterPlay", enterFields));
    }

    /// <summary>
    /// Apply any EnterPlay-targeted Replacement ability attached to the
    /// freshly-placed Unit. Returns true when a replacement fired (the
    /// caller should skip the normal EnterPlay/UnitEntered event
    /// emission — the replacement already moved the card into its
    /// alternate destination).
    /// </summary>
    private static bool ApplyEnterPlayReplacement(Entity unit, Entity player, Evaluator ev)
    {
        var enterEvent = new GameEvent("EnterPlay", new Dictionary<string, RtValue>
        {
            ["target"] = new RtEntityRef(unit.Id),
            ["player"] = new RtEntityRef(player.Id),
        });
        foreach (var ab in unit.Abilities)
        {
            if (ab.Kind != AbilityKind.Replacement) continue;
            if (ab.OnPattern is null) continue;
            if (!Interpreter.TryMatchPattern(ab.OnPattern, enterEvent, unit, out var bindings))
            {
                continue;
            }
            // Guard evaluation.
            if (ab.Named.TryGetValue("guard", out var guard))
            {
                var guardEnv = RtEnv.Empty.Extend("self", new RtEntityRef(unit.Id));
                if (player.Id != 0)
                {
                    guardEnv = guardEnv.Extend("owner", new RtEntityRef(player.Id));
                    guardEnv = guardEnv.Extend("controller", new RtEntityRef(player.Id));
                }
                var gv = ev.Eval(guard, guardEnv);
                if (gv is not RtBool rb || !rb.V) continue;
            }
            if (!ab.Named.TryGetValue("replace_with", out var rw)) continue;

            var env = RtEnv.Empty.Extend("self", new RtEntityRef(unit.Id))
                                 .Extend("owner", new RtEntityRef(player.Id))
                                 .Extend("controller", new RtEntityRef(player.Id));
            if (bindings.Count > 0) env = env.Extend(bindings);
            ev.Eval(rw, env);
            CastLog.RecordTrigger(unit, ab.OnPattern, enterEvent);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Copy <c>Triggered(...)</c> abilities off a card's declaration onto
    /// the runtime entity so <see cref="Interpreter.DispatchEvent"/> can walk
    /// them. Also expands the common trigger-shorthand forms
    /// (<c>OnEnter</c>, <c>EndOfClash</c>, <c>StartOfYourTurn</c>, etc.) to
    /// their underlying <c>Triggered(on: Event.X(...), effect: ...)</c>
    /// shape right here, since the ccgnf preprocessor doesn't always have
    /// <c>encoding/engine/02-trigger-shorthands.ccgnf</c> in scope when it
    /// processes card files (alphabetical sort puts <c>cards/</c> first).
    /// Static / Replacement entries are parsed too (so they show up in
    /// diagnostics and in the serializer) but Static currently rides on
    /// the KeywordRuntime direct-check path and Replacement is unwired.
    /// </summary>
    private static void AttachCardAbilities(Entity unit, Ast.AstCardDecl decl)
    {
        foreach (var f in decl.Body.Fields)
        {
            if (f.Key.Name != "abilities") continue;
            if (f.Value is not Ast.AstFieldExpr fe) continue;
            if (fe.Value is not Ast.AstListLit list) continue;
            foreach (var el in list.Elements)
            {
                if (el is not Ast.AstFunctionCall fc) continue;
                if (fc.Callee is not Ast.AstIdent id) continue;

                if (TryExpandShorthand(id.Name, fc) is AbilityInstance shorthand)
                {
                    shorthand = new AbilityInstance(
                        shorthand.Kind, unit.Id, shorthand.Named, shorthand.Positional);
                    unit.Abilities.Add(shorthand);
                    continue;
                }

                AbilityKind kind = id.Name switch
                {
                    "Triggered" => AbilityKind.Triggered,
                    "Static" => AbilityKind.Static,
                    "Replacement" => AbilityKind.Replacement,
                    _ => (AbilityKind)(-1),
                };
                if (kind == (AbilityKind)(-1)) continue;

                var named = new Dictionary<string, Ast.AstExpr>(StringComparer.Ordinal);
                var positional = new List<Ast.AstExpr>();
                foreach (var a in fc.Args)
                {
                    switch (a)
                    {
                        case Ast.AstArgNamed n: named[n.Name] = n.Value; break;
                        case Ast.AstArgBinding b: named[b.Name] = b.Value; break;
                        case Ast.AstArgPositional p: positional.Add(p.Value); break;
                    }
                }
                unit.Abilities.Add(new AbilityInstance(kind, unit.Id, named, positional));
            }
        }
    }

    private static readonly Ccgnf.Diagnostics.SourceSpan _shorthandSpan =
        Ccgnf.Diagnostics.SourceSpan.Unknown;

    /// <summary>
    /// Expands one of the trigger-shorthand macros defined in
    /// <c>encoding/engine/02-trigger-shorthands.ccgnf</c> into a Triggered
    /// <see cref="AbilityInstance"/>. Returns null for unrecognised callees
    /// so the caller falls through to the raw Triggered/Static/Replacement
    /// path.
    /// </summary>
    private static AbilityInstance? TryExpandShorthand(string name, Ast.AstFunctionCall fc)
    {
        Ast.AstExpr? effectExpr = FirstArgValue(fc);
        if (effectExpr is null) return null;

        Ast.AstExpr? pattern = name switch
        {
            "OnEnter"         => MakeEventPattern("EnterPlay",  ("target", MakeIdent("self"))),
            "OnPlayed"        => MakeEventPattern("CardPlayed", ("card",   MakeIdent("self"))),
            "StartOfYourTurn" => MakeEventPattern("PhaseBegin",
                                                  ("phase",  MakeIdent("Rise")),
                                                  ("player", MakeSelfMember("controller"))),
            "EndOfYourTurn"   => MakeEventPattern("PhaseBegin",
                                                  ("phase",  MakeIdent("Fall")),
                                                  ("player", MakeSelfMember("controller"))),
            "StartOfClash"    => MakeEventPattern("PhaseBegin", ("phase", MakeIdent("Clash"))),
            "EndOfClash"      => MakeEventPattern("PhaseEnd",   ("phase", MakeIdent("Clash"))),
            _ => null,
        };
        if (pattern is null)
        {
            // Lambda-filter shorthands need custom expansion: the filter
            // lambda's parameter name is reused as the event-field binding
            // so the lambda body can reference it naturally in the If-guard
            // we wrap around the effect.
            return TryExpandLambdaFilterShorthand(name, fc);
        }

        var named = new Dictionary<string, Ast.AstExpr>(StringComparer.Ordinal)
        {
            ["on"] = pattern,
            ["effect"] = effectExpr,
        };
        // OwnerId is fixed up by the caller.
        return new AbilityInstance(AbilityKind.Triggered, ownerId: 0, named, Array.Empty<Ast.AstExpr>());
    }

    /// <summary>
    /// Expands the two lambda-filter shorthand forms
    /// (<c>OnArenaEnter(filter: u -> ..., effect: ...)</c> and
    /// <c>OnCardPlayed(filter: c -> ..., effect: ...)</c>). The filter's
    /// parameter becomes the pattern binding name, so the lambda body
    /// can reference it directly inside the If-guard we wrap around the
    /// effect. The synthesised shape is:
    /// <code>
    /// Triggered(
    ///     on:     Event.EventName(&lt;event field&gt;=&lt;paramName&gt;),
    ///     effect: If(&lt;filter body&gt;, &lt;original effect&gt;, NoOp))
    /// </code>
    /// </summary>
    private static AbilityInstance? TryExpandLambdaFilterShorthand(string name, Ast.AstFunctionCall fc)
    {
        Ast.AstLambda? filter = null;
        Ast.AstExpr? effectExpr = null;
        foreach (var a in fc.Args)
        {
            if (a is not Ast.AstArgNamed n) continue;
            if (n.Name == "filter" && n.Value is Ast.AstLambda lam && lam.Parameters.Count == 1)
            {
                filter = lam;
            }
            else if (n.Name == "effect")
            {
                effectExpr = n.Value;
            }
        }
        if (filter is null || effectExpr is null) return null;

        var paramIdent = MakeIdent(filter.Parameters[0]);

        // Per-shorthand event pattern. OnArenaEnter also adds
        // arena=self.arena so the pattern matcher filters to the Unit's
        // own arena before we even evaluate the filter lambda.
        (Ast.AstExpr pattern, Ast.AstExpr guard) PatternAndGuard() => name switch
        {
            "OnArenaEnter" => (
                MakeEventPattern("EnterPlay",
                    ("target", paramIdent),
                    ("arena",  MakeSelfMember("arena"))),
                // Skip self-entry (OnArenaEnter's defining "another Unit").
                new Ast.AstBinaryOp(_shorthandSpan, "∧",
                    new Ast.AstBinaryOp(_shorthandSpan, "!=", paramIdent, MakeIdent("self")),
                    filter.Body)),
            "OnCardPlayed" => (
                MakeEventPattern("CardPlayed",
                    ("card",   paramIdent),
                    ("player", MakeSelfMember("controller"))),
                filter.Body),
            _ => (null!, null!),
        };
        var (patternExpr, guardExpr) = PatternAndGuard();
        if (patternExpr is null) return null;

        var noOp = new Ast.AstFunctionCall(_shorthandSpan,
            MakeIdent("NoOp"), Array.Empty<Ast.AstArg>());
        var ifWrapped = new Ast.AstFunctionCall(_shorthandSpan,
            MakeIdent("If"), new List<Ast.AstArg>
            {
                new Ast.AstArgPositional(_shorthandSpan, guardExpr),
                new Ast.AstArgPositional(_shorthandSpan, effectExpr),
                new Ast.AstArgPositional(_shorthandSpan, noOp),
            });

        var named = new Dictionary<string, Ast.AstExpr>(StringComparer.Ordinal)
        {
            ["on"] = patternExpr,
            ["effect"] = ifWrapped,
        };
        return new AbilityInstance(AbilityKind.Triggered, ownerId: 0, named, Array.Empty<Ast.AstExpr>());
    }

    private static Ast.AstExpr? FirstArgValue(Ast.AstFunctionCall fc)
    {
        // Shorthand calls use either OnX(effect_expr) or OnX(effect: ...).
        // Accept both; for 2-arg OnArenaEnter / OnCardPlayed the `effect`
        // arg is what we pick up; OnArenaEnter's `filter` isn't wired yet
        // (it requires lambda-level pattern matching).
        foreach (var a in fc.Args)
        {
            if (a is Ast.AstArgNamed { Name: "effect" } n) return n.Value;
        }
        foreach (var a in fc.Args)
        {
            if (a is Ast.AstArgPositional p) return p.Value;
        }
        return null;
    }

    private static Ast.AstIdent MakeIdent(string name) =>
        new(_shorthandSpan, name);

    private static Ast.AstMemberAccess MakeSelfMember(string member) =>
        new(_shorthandSpan, MakeIdent("self"), member);

    private static Ast.AstFunctionCall MakeEventPattern(
        string eventName,
        params (string Field, Ast.AstExpr Value)[] args)
    {
        var callee = new Ast.AstMemberAccess(_shorthandSpan, MakeIdent("Event"), eventName);
        var synArgs = new List<Ast.AstArg>(args.Length);
        foreach (var (field, value) in args)
        {
            synArgs.Add(new Ast.AstArgNamed(_shorthandSpan, field, value));
        }
        return new Ast.AstFunctionCall(_shorthandSpan, callee, synArgs);
    }

    private static int GetCardCost(Ast.AstCardDecl decl) => GetCardIntField(decl, "cost");

    /// <summary>
    /// Effective cost at the play step (GrammarSpec §6.2):
    /// base cost − persistent <c>base_cost_reduction</c> (Phantom's
    /// per-return rebate) − Surge rebate (−1 if another Echo of the same
    /// faction was pushed this turn), floored at 0.
    /// </summary>
    private static int ComputeEffectiveCost(Evaluator ev, Entity player, Entity card, Ast.AstCardDecl decl)
    {
        int cost = GetCardCost(decl);
        cost -= card.Counters.GetValueOrDefault("base_cost_reduction", 0);

        if (HasKeywordOnDecl(decl, "Surge"))
        {
            // "costs 1 less if another EMBER Echo was Pushed this turn."
            // With only ResonanceField-wide counts (no per-turn history yet),
            // the interim heuristic is "you have >=2 echoes of any of this
            // card's factions on the field" — two echoes means "another
            // echo exists besides this card's own push".
            var factions = KeywordRuntime.ReadFactions(decl);
            foreach (var f in factions)
            {
                if (KeywordRuntime.CountEcho(player, f) >= 2)
                {
                    cost -= 1;
                    break;
                }
            }
        }
        _ = ev;
        return Math.Max(0, cost);
    }

    private static bool HasKeywordOnDecl(Ast.AstCardDecl decl, string name)
    {
        foreach (var (n, _) in KeywordRuntime.ReadKeywords(decl))
        {
            if (n == name) return true;
        }
        return false;
    }

    private static int GetCardIntField(Ast.AstCardDecl decl, string name)
    {
        foreach (var f in decl.Body.Fields)
        {
            if (f.Key.Name != name) continue;
            if (f.Value is Ast.AstFieldExpr fe && fe.Value is Ast.AstIntLit i) return i.Value;
        }
        return 0;
    }

    private static string? GetCardType(Ast.AstCardDecl decl)
    {
        foreach (var f in decl.Body.Fields)
        {
            if (f.Key.Name != "type") continue;
            if (f.Value is Ast.AstFieldExpr fe && fe.Value is Ast.AstIdent id) return id.Name;
        }
        return null;
    }

    private static Ast.AstExpr? GetCardOnResolve(Ast.AstCardDecl decl)
    {
        foreach (var f in decl.Body.Fields)
        {
            if (f.Key.Name != "abilities") continue;
            if (f.Value is not Ast.AstFieldExpr fe) continue;
            if (fe.Value is not Ast.AstListLit list) continue;
            foreach (var el in list.Elements)
            {
                if (el is Ast.AstFunctionCall fc
                    && fc.Callee is Ast.AstIdent id
                    && id.Name == "OnResolve"
                    && fc.Args.Count > 0)
                {
                    return ExprOf(fc.Args[0]);
                }
            }
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // ResolveClashPhase — walk arenas, ask each active-player Unit attack/
    // hold, then push Force → opponent's same-arena Conduit for attackers.
    // -------------------------------------------------------------------------

    private static RtValue ResolveClashPhase(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        // Named arg: active_player: p. Legacy positional also accepted.
        AstExpr? playerExpr = null;
        foreach (var a in call.Args)
        {
            switch (a)
            {
                case AstArgNamed { Name: "active_player" } n: playerExpr = n.Value; break;
                case AstArgPositional pa: playerExpr ??= pa.Value; break;
            }
        }
        if (playerExpr is null) return new RtVoid();

        var playerVal = ev.Eval(playerExpr, env);
        if (playerVal is not RtEntityRef er ||
            !ev.State.Entities.TryGetValue(er.Id, out var attacker))
        {
            return new RtVoid();
        }
        var opponent = ev.State.Players.FirstOrDefault(p => p.Id != attacker.Id);

        // Before iterating Units for attack/hold prompts, auto-mark every
        // Phantom-keyword Unit belonging to either player as phantoming
        // for this clash. The authored macro is a Choice-driven StartOfClash
        // fade — v1 simplification is always-fade, which is what the
        // existing probes and card wording assume.
        foreach (var e in ev.State.Entities.Values.ToList())
        {
            if (!KeywordRuntime.HasKeyword(e, "Phantom")) continue;
            if (!e.Characteristics.TryGetValue("in_play", out var ipp) ||
                ipp is not RtBool ipb || !ipb.V) continue;
            e.Tags.Add("phantoming");
        }

        // Per-arena: attack/hold prompt for each active-player Unit whose
        // `arena` parameter matches the arena's `pos`. Holds are a no-op;
        // attackers accumulate into a list, then damage resolves after all
        // prompts so declarations are atomic per arena (plan's simplified §7.1).
        foreach (var arena in ev.State.Arenas)
        {
            if (!arena.Parameters.TryGetValue("pos", out var posVal) ||
                posVal is not RtSymbol pos)
            {
                continue;
            }

            // Phantoming Units are off the board for this Clash; skip
            // their attack/hold prompt entirely.
            var units = UnitsOnArena(ev, attacker.Id, pos.Name)
                .Where(u => !u.Tags.Contains("phantoming"))
                .ToList();
            if (units.Count == 0) continue;

            ev.State.PendingEvents.Enqueue(new GameEvent("ClashBegin",
                new Dictionary<string, RtValue>
                {
                    ["arena"] = new RtEntityRef(arena.Id),
                    ["active_player"] = new RtEntityRef(attacker.Id),
                }));

            var attackers = new List<Entity>();
            foreach (var unit in units)
            {
                var legal = new List<LegalAction>
                {
                    new("declare_attacker", "attack", new Dictionary<string, string>
                    {
                        ["unitId"] = unit.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["cardName"] = unit.DisplayName,
                        ["arena"] = pos.Name,
                        ["force"] = unit.Counters.GetValueOrDefault("force", 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    }),
                    new("declare_attacker", "hold", new Dictionary<string, string>
                    {
                        ["unitId"] = unit.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["cardName"] = unit.DisplayName,
                        ["arena"] = pos.Name,
                    }),
                };
                var request = new InputRequest(
                    Prompt: $"Clash.{pos.Name}({unit.DisplayName})",
                    PlayerId: attacker.Id,
                    LegalActions: legal);
                var choice = ev.Scheduler.Inputs.Next(request);
                string label = choice switch
                {
                    RtSymbol s => s.Name,
                    RtString s => s.V,
                    _ => choice.ToString() ?? "",
                };
                if (label == "attack") attackers.Add(unit);
            }

            // Per-Arena formula (encoding/engine/09-clash.ccgnf:46-51):
            //   incoming[defender] = max(0, projected_force[attacker]
            //                           - fortification[defender])
            // Projected Force is the attacker-side sum after Sentinel zeroes
            // its contributors; fortification is the defender-side sum of
            // effective Ramparts (Fortify bonus included) plus any Sentinel
            // redirect of Force into Fortification. A single DamageDealt
            // event is emitted per Arena so later triggers see one damage
            // beat per Arena rather than one per attacker.
            if (opponent is not null && attackers.Count > 0)
            {
                Entity? conduit = FindConduit(ev, opponent.Id, pos.Name);
                int projectedForce = 0;
                foreach (var a in attackers)
                {
                    projectedForce += KeywordRuntime.GetClashProjectedForce(a, ev.State);
                }
                int fortification = DefenderArenaFortification(ev, opponent.Id, pos.Name);
                int incoming = Math.Max(0, projectedForce - fortification);

                if (conduit is not null && incoming > 0)
                {
                    int before = conduit.Counters.GetValueOrDefault("integrity", 0);
                    conduit.Counters["integrity"] = Math.Max(0, before - incoming);
                    ev.State.PendingEvents.Enqueue(new GameEvent("DamageDealt",
                        new Dictionary<string, RtValue>
                        {
                            ["source"] = new RtEntityRef(attacker.Id),
                            ["target"] = new RtEntityRef(conduit.Id),
                            ["counter"] = new RtSymbol("integrity"),
                            ["amount"] = new RtInt(incoming),
                            ["arena"] = new RtEntityRef(arena.Id),
                            ["projected_force"] = new RtInt(projectedForce),
                            ["fortification"] = new RtInt(fortification),
                        }));
                }
            }

            ev.State.PendingEvents.Enqueue(new GameEvent("ClashEnd",
                new Dictionary<string, RtValue>
                {
                    ["arena"] = new RtEntityRef(arena.Id),
                    ["attackers"] = new RtInt(attackers.Count),
                }));
        }
        // One PhaseEnd(phase=Clash) at the end of the whole phase, after
        // every arena's Clash window has closed. EndOfClash-shorthand
        // triggers on Units match on this pattern. Enqueued BEFORE any
        // phantoming-return cleanup so PhantomReturn-listening triggers
        // (e.g. BlankfaceCultist's Pilfer) see the event order they
        // expect.
        ev.State.PendingEvents.Enqueue(new GameEvent("PhaseEnd",
            new Dictionary<string, RtValue>
            {
                ["phase"] = new RtSymbol("Clash"),
                ["player"] = new RtEntityRef(attacker.Id),
            }));
        return new RtVoid();
    }

    private static List<Entity> UnitsOnArena(Evaluator ev, int playerId, string arenaPos)
    {
        var units = new List<Entity>();
        foreach (var e in ev.State.Entities.Values)
        {
            if (e.OwnerId != playerId) continue;
            if (!e.Characteristics.TryGetValue("in_play", out var ip) ||
                ip is not RtBool rb || !rb.V) continue;
            if (!e.Characteristics.TryGetValue("type", out var t) ||
                t is not RtSymbol ts || ts.Name != "Unit") continue;
            if (!e.Parameters.TryGetValue("arena", out var av) ||
                av is not RtSymbol ap || ap.Name != arenaPos) continue;
            units.Add(e);
        }
        return units;
    }

    /// <summary>
    /// Sum of per-Unit Fortification on the defender's side of one Arena —
    /// effective Ramparts plus any Sentinel Force redirect (both computed
    /// per-Unit by <see cref="KeywordRuntime.GetClashFortification"/>). This
    /// is what the attacker's projected Force must overcome before Conduit
    /// integrity starts dropping.
    /// </summary>
    private static int DefenderArenaFortification(Evaluator ev, int defenderId, string arenaPos)
    {
        int total = 0;
        foreach (var unit in UnitsOnArena(ev, defenderId, arenaPos))
        {
            total += KeywordRuntime.GetClashFortification(unit, ev.State);
        }
        return total;
    }

    private static Entity? FindConduit(Evaluator ev, int ownerId, string arenaPos)
    {
        foreach (var e in ev.State.Entities.Values)
        {
            if (e.Kind != "Conduit") continue;
            if (e.OwnerId != ownerId) continue;
            if (e.Tags.Contains("collapsed")) continue;
            if (!e.Parameters.TryGetValue("arena", out var av) ||
                av is not RtSymbol ap || ap.Name != arenaPos) continue;
            return e;
        }
        return null;
    }

    // -------------------------------------------------------------------------

    private static RtValue BeginPhase(AstFunctionCall call, RtEnv env, Evaluator ev)
    {
        // BeginPhase(phase, player=p) — first positional is the phase symbol,
        // second arg (positional or `player=`) is the acting player. Expands
        // to EmitEvent(Event.PhaseBegin(phase=..., player=...)) so the same
        // trigger pattern matches Setup's kick-off as well as the end-of-phase
        // advances chained from Rise → Channel → Clash → Fall → Pass.
        AstExpr? phaseExpr = null;
        AstExpr? playerExpr = null;
        foreach (var a in call.Args)
        {
            switch (a)
            {
                case AstArgPositional p:
                    if (phaseExpr is null) phaseExpr = p.Value;
                    else if (playerExpr is null) playerExpr = p.Value;
                    break;
                case AstArgNamed n when n.Name == "phase": phaseExpr = n.Value; break;
                case AstArgNamed n when n.Name == "player": playerExpr = n.Value; break;
            }
        }
        if (phaseExpr is null) return new RtVoid();

        var fields = new Dictionary<string, RtValue>
        {
            ["phase"] = ev.Eval(phaseExpr, env),
        };
        if (playerExpr is not null) fields["player"] = ev.Eval(playerExpr, env);

        ev.State.PendingEvents.Enqueue(new GameEvent("PhaseBegin", fields));
        return new RtVoid();
    }

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
