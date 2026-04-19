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

            // ----- Mulligan / MoveTo — still stubbed (lands alongside full Target) -----
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
        foreach (var entity in snapshot)
        {
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
                int cost = GetCardCost(decl);
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
    }

    private static int GetCardCost(Ast.AstCardDecl decl) => GetCardIntField(decl, "cost");

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

            var units = UnitsOnArena(ev, attacker.Id, pos.Name);
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

            // Damage: each attacker's Force hits the opponent's Conduit
            // sitting in the same arena (if any). Multiple attackers in one
            // arena stack straight onto the same conduit.
            if (opponent is not null && attackers.Count > 0)
            {
                Entity? conduit = FindConduit(ev, opponent.Id, pos.Name);
                foreach (var a in attackers)
                {
                    int force = a.Counters.GetValueOrDefault("force", 0);
                    if (conduit is null || force <= 0) continue;
                    int before = conduit.Counters.GetValueOrDefault("integrity", 0);
                    conduit.Counters["integrity"] = Math.Max(0, before - force);
                    ev.State.PendingEvents.Enqueue(new GameEvent("DamageDealt",
                        new Dictionary<string, RtValue>
                        {
                            ["source"] = new RtEntityRef(a.Id),
                            ["target"] = new RtEntityRef(conduit.Id),
                            ["counter"] = new RtSymbol("integrity"),
                            ["amount"] = new RtInt(force),
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
