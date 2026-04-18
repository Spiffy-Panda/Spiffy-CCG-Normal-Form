using Ccgnf.Ast;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ccgnf.Interpreter;

/// <summary>
/// Turns a validated <see cref="AstFile"/> into a concrete <see cref="GameState"/>
/// ready to receive its first event. This is the declarative → runtime bridge:
/// entity declarations with a <c>for</c>-clause become live instances, plain
/// declarations become singletons, and <see cref="AstEntityAugment"/>
/// directives attach abilities to their target entity by name.
/// <para>
/// Parameterized declarations with no <c>for</c>-clause (e.g.,
/// <c>Entity Conduit[owner, arena]</c>) remain templates — they're
/// instantiated at runtime via <c>InstantiateEntity</c>. Anything flagged
/// <c>lifetime: ephemeral</c> (PlayBinding) is similarly skipped.
/// </para>
/// </summary>
public sealed class StateBuilder
{
    private readonly ILogger<StateBuilder> _log;

    public StateBuilder(ILogger<StateBuilder>? log = null)
    {
        _log = log ?? NullLogger<StateBuilder>.Instance;
    }

    public GameState Build(AstFile file, Scheduler scheduler)
    {
        var state = new GameState();
        var evaluator = new Evaluator(state, scheduler);

        // Pass 1: instantiate entities.
        foreach (var decl in file.Declarations.OfType<AstEntityDecl>())
        {
            InstantiateDecl(decl, state, evaluator);
        }

        // Pass 2: attach abilities from augmentations.
        foreach (var aug in file.Declarations.OfType<AstEntityAugment>())
        {
            AttachAugmentation(aug, state);
        }

        _log.LogInformation(
            "StateBuilder: {Players} players, {Arenas} arenas, {Entities} total entities, " +
            "{Abilities} abilities on Game",
            state.Players.Count, state.Arenas.Count, state.Entities.Count,
            state.Game?.Abilities.Count ?? 0);

        return state;
    }

    // -------------------------------------------------------------------------

    private void InstantiateDecl(AstEntityDecl decl, GameState state, Evaluator ev)
    {
        // Ephemeral templates are not instantiated at setup time.
        if (IsEphemeral(decl)) return;

        if (decl.ForClause is not null)
        {
            var source = ev.Eval(decl.ForClause.Source, RtEnv.Empty);
            foreach (var item in Evaluator.AsList(source))
            {
                var frame = new List<(string, RtValue)>
                {
                    (decl.ForClause.Variable, item),
                };
                var instanceName = ComputeInstanceName(decl.Name, item);
                var entity = state.AllocateEntity(decl.Name, instanceName);
                entity.Parameters[decl.ForClause.Variable] = item;
                PopulateBody(entity, decl.Body, ev, RtEnv.Empty.Extend(frame));
                Register(state, entity, decl);
            }
            return;
        }

        // No for-clause. If the declaration has index parameters (e.g.,
        // `Entity Conduit[owner, arena]`), it's a template instantiated at
        // runtime. Otherwise it's a singleton.
        if (decl.IndexParams.Count > 0) return;

        var singleton = state.AllocateEntity(decl.Name, decl.Name);
        PopulateBody(singleton, decl.Body, ev, RtEnv.Empty);
        Register(state, singleton, decl);
    }

    private static bool IsEphemeral(AstEntityDecl decl)
    {
        foreach (var f in decl.Body.Fields)
        {
            if (f.Key.Name == "lifetime" && f.Value is AstFieldExpr fe &&
                fe.Value is AstIdent { Name: "ephemeral" })
            {
                return true;
            }
        }
        return false;
    }

    private static string ComputeInstanceName(string baseName, RtValue index)
    {
        return index switch
        {
            RtSymbol s => baseName + s.Name,
            RtInt i => baseName + i.V,
            RtString s => baseName + s.V,
            _ => baseName + index,
        };
    }

    private static void Register(GameState state, Entity entity, AstEntityDecl decl)
    {
        state.NamedEntities[entity.DisplayName] = entity;
        switch (entity.Kind)
        {
            case "Game":
                state.Game = entity;
                break;
            case "Player":
                state.Players.Add(entity);
                break;
            case "Arena":
                state.Arenas.Add(entity);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Body population — characteristics, counters, zones.
    // -------------------------------------------------------------------------

    private void PopulateBody(Entity entity, AstBlock body, Evaluator ev, RtEnv env)
    {
        foreach (var field in body.Fields)
        {
            switch (field.Key.Name)
            {
                case "kind":
                    // The entity's kind is set at construction; the field is
                    // informational for the v1 runtime.
                    break;

                case "characteristics":
                    PopulateCharacteristics(entity, field.Value, ev, env);
                    break;

                case "counters":
                    PopulateCounters(entity, field.Value, ev, env);
                    break;

                case "zones":
                    PopulateZones(entity, field.Value, ev, env);
                    break;

                case "abilities":
                    // Empty list at declaration — real abilities arrive via
                    // augmentations.
                    break;

                default:
                    // Unknown field — store as a characteristic for later
                    // inspection. Cheap, keeps the state builder lenient.
                    entity.Characteristics[field.Key.Name] = EvaluateField(field.Value, ev, env);
                    break;
            }
        }
    }

    private void PopulateCharacteristics(Entity entity, AstFieldValue value, Evaluator ev, RtEnv env)
    {
        if (value is AstFieldBlock fb)
        {
            foreach (var f in fb.Block.Fields)
            {
                entity.Characteristics[f.Key.Name] = EvaluateField(f.Value, ev, env);
            }
            return;
        }
        // Brace expression — treat each field entry identically.
        var expr = InnerExpr(value);
        if (expr is AstBraceExpr be)
        {
            foreach (var e in be.Entries.OfType<AstBraceField>())
            {
                entity.Characteristics[e.Field.Key.Name] = EvaluateField(e.Field.Value, ev, env);
            }
        }
    }

    private void PopulateCounters(Entity entity, AstFieldValue value, Evaluator ev, RtEnv env)
    {
        if (value is AstFieldBlock fb)
        {
            foreach (var f in fb.Block.Fields)
            {
                var v = EvaluateField(f.Value, ev, env);
                if (v is RtInt i) entity.Counters[f.Key.Name] = i.V;
                else entity.Counters[f.Key.Name] = 0;
            }
            return;
        }
        var expr = InnerExpr(value);
        if (expr is AstBraceExpr be)
        {
            foreach (var e in be.Entries.OfType<AstBraceField>())
            {
                var v = EvaluateField(e.Field.Value, ev, env);
                entity.Counters[e.Field.Key.Name] = v is RtInt i ? i.V : 0;
            }
        }
    }

    private void PopulateZones(Entity entity, AstFieldValue value, Evaluator ev, RtEnv env)
    {
        AstBraceExpr? be = value switch
        {
            AstFieldBlock fb => new AstBraceExpr(
                fb.Span,
                fb.Block.Fields.Select(f => (AstBraceEntry)new AstBraceField(f.Span, f)).ToList()),
            AstFieldExpr fe => fe.Value as AstBraceExpr,
            AstFieldTyped ft => ft.Value as AstBraceExpr,
            _ => null,
        };
        if (be is null) return;

        foreach (var entry in be.Entries.OfType<AstBraceField>())
        {
            string name = entry.Field.Key.Name;
            var zoneDecl = InnerExpr(entry.Field.Value);
            var zone = BuildZone(name, zoneDecl);
            entity.Zones[name] = zone;
        }
    }

    private static Zone BuildZone(string name, AstExpr? decl)
    {
        if (decl is AstFunctionCall call && call.Callee is AstIdent { Name: "Zone" })
        {
            var order = ZoneOrder.Unordered;
            int? capacity = null;
            foreach (var a in call.Args.OfType<AstArgNamed>())
            {
                if (a.Name == "order" && a.Value is AstIdent orderId)
                {
                    order = orderId.Name switch
                    {
                        "sequential" => ZoneOrder.Sequential,
                        "FIFO" => ZoneOrder.FIFO,
                        "LIFO" => ZoneOrder.LIFO,
                        _ => ZoneOrder.Unordered,
                    };
                }
                else if (a.Name == "capacity" && a.Value is AstIntLit intLit)
                {
                    capacity = intLit.Value;
                }
            }
            return new Zone(name, order, capacity);
        }
        return new Zone(name);
    }

    private static AstExpr? InnerExpr(AstFieldValue v) => v switch
    {
        AstFieldExpr fe => fe.Value,
        AstFieldTyped ft => ft.Value,
        _ => null,
    };

    private static RtValue EvaluateField(AstFieldValue v, Evaluator ev, RtEnv env)
    {
        var expr = InnerExpr(v);
        return expr is null ? new RtVoid() : ev.Eval(expr, env);
    }

    // -------------------------------------------------------------------------
    // Augmentations → abilities.
    // -------------------------------------------------------------------------

    private void AttachAugmentation(AstEntityAugment aug, GameState state)
    {
        if (aug.Target.Segments.Count < 2) return;
        var first = aug.Target.Segments[0].Name;
        var last = aug.Target.Segments[^1].Name;
        if (last != "abilities") return;

        // We only attach to singleton entities in v1 — Game is the primary
        // target. Player.abilities is the Activated PlayCard; not needed.
        if (!state.NamedEntities.TryGetValue(first, out var entity)) return;

        if (aug.Value is AstFunctionCall call && call.Callee is AstIdent kindId)
        {
            if (!TryParseAbilityKind(kindId.Name, out var kind)) return;

            var named = new Dictionary<string, AstExpr>(StringComparer.Ordinal);
            var positional = new List<AstExpr>();
            foreach (var a in call.Args)
            {
                switch (a)
                {
                    case AstArgNamed n: named[n.Name] = n.Value; break;
                    case AstArgBinding b: named[b.Name] = b.Value; break;
                    case AstArgPositional p: positional.Add(p.Value); break;
                }
            }

            entity.Abilities.Add(new AbilityInstance(kind, entity.Id, named, positional));
            _log.LogDebug("Attached {Kind} ability to {Entity}", kind, entity.DisplayName);
        }
    }

    private static bool TryParseAbilityKind(string name, out AbilityKind kind)
    {
        switch (name)
        {
            case "Triggered":  kind = AbilityKind.Triggered; return true;
            case "Static":     kind = AbilityKind.Static; return true;
            case "OnResolve":  kind = AbilityKind.OnResolve; return true;
            case "Replacement": kind = AbilityKind.Replacement; return true;
            case "Activated":  kind = AbilityKind.Activated; return true;
            default: kind = AbilityKind.Triggered; return false;
        }
    }
}
