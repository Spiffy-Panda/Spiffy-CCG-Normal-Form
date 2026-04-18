using Ccgnf.Ast;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ccgnf.Interpreter;

/// <summary>
/// Walks an AST expression and produces an <see cref="RtValue"/>, side-effecting
/// the <see cref="GameState"/> where builtins are expected to mutate. v1 has no
/// separate "effect term" algebra — a call to an atomic-op builtin applies its
/// mutation immediately and returns <see cref="RtVoid"/>. Control-flow builtins
/// (Sequence, ForEach, If, Choice) take their arguments as AST and decide what
/// to evaluate, so <c>If(p, a, b)</c> evaluates only one branch.
/// </summary>
public sealed class Evaluator
{
    private readonly ILogger<Evaluator> _log;

    public GameState State { get; }
    public Scheduler Scheduler { get; }
    public ILogger<Evaluator> Log => _log;

    public Evaluator(GameState state, Scheduler scheduler, ILogger<Evaluator>? log = null)
    {
        State = state;
        Scheduler = scheduler;
        _log = log ?? NullLogger<Evaluator>.Instance;
    }

    public RtValue Eval(AstExpr expr, RtEnv env)
    {
        switch (expr)
        {
            case AstIntLit i:
                return new RtInt(i.Value);

            case AstStringLit s:
                return new RtString(s.Value);

            case AstIdent id:
                return EvalIdent(id, env);

            case AstBinaryOp b:
                return EvalBinary(b, env);

            case AstUnaryOp u:
                return EvalUnary(u, env);

            case AstMemberAccess m:
                return EvalMember(m, env);

            case AstIndex idx:
                return EvalIndex(idx, env);

            case AstFunctionCall c:
                return EvalCall(c, env);

            case AstLambda la:
                return new RtLambda(la.Parameters, la.Body);

            case AstParen p:
                return p.Elements.Count == 1
                    ? Eval(p.Elements[0], env)
                    : new RtTuple(p.Elements.Select(e => Eval(e, env)).ToList());

            case AstBraceExpr be:
                return EvalBrace(be, env);

            case AstListLit ll:
                return new RtList(ll.Elements.Select(e => Eval(e, env)).ToList());

            case AstRangeLit rl:
                return EvalRange(rl, env);

            case AstIfExpr ie:
                return AsBool(Eval(ie.Condition, env))
                    ? Eval(ie.Then, env)
                    : Eval(ie.Else, env);

            case AstSwitchExpr sw:
                return EvalSwitch(sw, env);

            case AstCondExpr co:
                return EvalCond(co, env);

            case AstWhenExpr wh:
                return Eval(wh.Effect, env);

            case AstLetExpr let:
                var bound = Eval(let.Value, env);
                return Eval(let.Body, env.Extend(let.Variable, bound));

            default:
                _log.LogWarning("Evaluator: unsupported AST node {Kind}", expr.GetType().Name);
                return new RtVoid();
        }
    }

    // -------------------------------------------------------------------------
    // Identifier resolution
    // -------------------------------------------------------------------------

    private RtValue EvalIdent(AstIdent id, RtEnv env)
    {
        // 1. Lexical scope wins: lambda parameters, let bindings, ForEach vars.
        if (env.TryLookup(id.Name, out var v)) return v;

        // 2. Keyword literals that the grammar routes through name/IDENT.
        switch (id.Name)
        {
            case "true": return new RtBool(true);
            case "false": return new RtBool(false);
            case "None": return new RtNone();
            case "Unbound": return new RtUnbound();
            case "NoOp": return new RtNoOp();
        }

        // 3. Named entities: Player1, Player2, ArenaLeft, Game.
        if (State.NamedEntities.TryGetValue(id.Name, out var ent))
        {
            return new RtEntityRef(ent.Id);
        }

        // 4. Any other bare ident becomes a symbol — Left, Right, Rise, EMBER,
        //    Unit, etc. The runtime doesn't enforce a fixed symbol table; the
        //    Validator is responsible for catching typos.
        return new RtSymbol(id.Name);
    }

    // -------------------------------------------------------------------------
    // Member / index access
    // -------------------------------------------------------------------------

    private RtValue EvalMember(AstMemberAccess m, RtEnv env)
    {
        // Special case: Event.Foo as a bare value means "an event of type Foo
        // with no fields". Used as a pattern in on: and as a zero-payload
        // constructor in EmitEvent.
        if (m.Target is AstIdent { Name: "Event" })
        {
            return new RtEventLit(m.Member, new Dictionary<string, RtValue>());
        }

        var target = Eval(m.Target, env);
        return LookupMember(target, m.Member);
    }

    internal RtValue LookupMember(RtValue target, string member)
    {
        if (target is RtEntityRef er && State.Entities.TryGetValue(er.Id, out var entity))
        {
            // Characteristics first, then counters, then zones, then parameters,
            // then bare-fall-through symbol.
            if (entity.Characteristics.TryGetValue(member, out var ch)) return ch;
            if (entity.Counters.TryGetValue(member, out var co)) return new RtInt(co);
            if (entity.Zones.TryGetValue(member, out var _)) return new RtZoneRef(entity.Id, member);
            if (entity.Parameters.TryGetValue(member, out var pr)) return pr;
        }
        _log.LogTrace("Unresolved member {Member} on {Target}", member, target);
        return new RtSymbol(member);
    }

    private RtValue EvalIndex(AstIndex idx, RtEnv env)
    {
        var target = Eval(idx.Target, env);
        var indices = idx.Indices.Select(e => Eval(e, env)).ToList();

        if (target is RtList list && indices.Count == 1 && indices[0] is RtInt ri)
        {
            if (ri.V < 0 || ri.V >= list.Elements.Count)
            {
                _log.LogWarning("Index {Index} out of range for list of {Count}",
                    ri.V, list.Elements.Count);
                return new RtVoid();
            }
            return list.Elements[ri.V];
        }

        // Entity indexing like Arena[Left] — look up by concatenated name.
        if (target is RtSymbol sym)
        {
            var key = sym.Name + string.Join("", indices.Select(FormatIndex));
            if (State.NamedEntities.TryGetValue(key, out var e))
            {
                return new RtEntityRef(e.Id);
            }
        }

        _log.LogTrace("Unsupported index on {Target} with {Count} indices", target, indices.Count);
        return new RtVoid();
    }

    private static string FormatIndex(RtValue v) => v switch
    {
        RtSymbol s => s.Name,
        RtInt i => i.V.ToString(),
        RtString s => s.V,
        _ => v.ToString() ?? "?",
    };

    // -------------------------------------------------------------------------
    // Operators
    // -------------------------------------------------------------------------

    private RtValue EvalBinary(AstBinaryOp b, RtEnv env)
    {
        // Short-circuit for logical operators.
        switch (b.Op)
        {
            case "and":
            case "∧":
                return AsBool(Eval(b.Left, env)) && AsBool(Eval(b.Right, env))
                    ? new RtBool(true)
                    : new RtBool(false);
            case "or":
            case "∨":
                return AsBool(Eval(b.Left, env)) || AsBool(Eval(b.Right, env))
                    ? new RtBool(true)
                    : new RtBool(false);
        }

        var l = Eval(b.Left, env);
        var r = Eval(b.Right, env);

        switch (b.Op)
        {
            case "+": return new RtInt(AsInt(l) + AsInt(r));
            case "-": return new RtInt(AsInt(l) - AsInt(r));
            case "*": return new RtInt(AsInt(l) * AsInt(r));
            case "/": return new RtInt(AsInt(l) / AsInt(r));

            case "==": return new RtBool(RtEquals(l, r));
            case "!=": return new RtBool(!RtEquals(l, r));
            case "<":  return new RtBool(AsInt(l) < AsInt(r));
            case ">":  return new RtBool(AsInt(l) > AsInt(r));
            case "<=": return new RtBool(AsInt(l) <= AsInt(r));
            case ">=": return new RtBool(AsInt(l) >= AsInt(r));

            case "in":
            case "∈":
                return new RtBool(SetContains(r, l));

            case "⊆":
                return new RtBool(IsSubset(l, r));

            case "×":
                return CartesianProduct(l, r);

            case "∩":
                return Intersect(l, r);

            default:
                _log.LogWarning("Unsupported binary operator {Op}", b.Op);
                return new RtVoid();
        }
    }

    private RtValue EvalUnary(AstUnaryOp u, RtEnv env)
    {
        var v = Eval(u.Operand, env);
        return u.Op switch
        {
            "-" => new RtInt(-AsInt(v)),
            "not" or "¬" => new RtBool(!AsBool(v)),
            _ => v,
        };
    }

    // -------------------------------------------------------------------------
    // Function calls — dispatched through Builtins
    // -------------------------------------------------------------------------

    private RtValue EvalCall(AstFunctionCall call, RtEnv env)
    {
        if (call.Callee is AstIdent id)
        {
            if (Builtins.TryDispatch(id.Name, call, env, this, out var result))
            {
                return result;
            }
            // Unknown function — ignore and return void. Macros are already
            // expanded; anything still unresolved here is a v1 gap (e.g., a
            // card-text helper we haven't wired). Log and move on.
            _log.LogDebug("Unknown function {Name}; returning void", id.Name);
            return new RtVoid();
        }

        if (call.Callee is AstMemberAccess ma && ma.Target is AstIdent { Name: "Event" })
        {
            return BuildEventLiteral(ma.Member, call.Args, env);
        }

        // A zero-arg macro declared as `define Foo() = body` is invoked as
        // `Foo()`, which the preprocessor currently expands by substituting
        // Foo's body and leaving the trailing `()` in place. We land here
        // with Callee=<the expanded body> and no args — evaluate the callee
        // and return its value; the body already produced any side effects.
        if (call.Args.Count == 0)
        {
            return Eval(call.Callee, env);
        }

        _log.LogDebug("Unsupported call form {Form}", call.Callee.GetType().Name);
        return new RtVoid();
    }

    private RtEventLit BuildEventLiteral(string typeName, IReadOnlyList<AstArg> args, RtEnv env)
    {
        var fields = new Dictionary<string, RtValue>();
        foreach (var a in args)
        {
            switch (a)
            {
                case AstArgNamed n:
                    fields[n.Name] = Eval(n.Value, env);
                    break;
                case AstArgBinding b:
                    fields[b.Name] = Eval(b.Value, env);
                    break;
                case AstArgPositional:
                    _log.LogTrace("Positional arg on Event.{Name} ignored", typeName);
                    break;
            }
        }
        return new RtEventLit(typeName, fields);
    }

    // -------------------------------------------------------------------------
    // Brace / range / switch / cond
    // -------------------------------------------------------------------------

    private RtValue EvalBrace(AstBraceExpr be, RtEnv env)
    {
        // A brace expression with no fields is a set literal. A brace with
        // fields is a map-like value. v1 treats both uniformly: we represent
        // field-bearing braces as an RtList of key/value tuples since no
        // current caller inspects them by key at runtime except Switch which
        // takes its arms from the AST directly.
        var elements = new List<RtValue>();
        foreach (var entry in be.Entries)
        {
            switch (entry)
            {
                case AstBraceValue bv:
                    elements.Add(Eval(bv.Value, env));
                    break;
                case AstBraceField bf:
                    elements.Add(new RtTuple(new RtValue[]
                    {
                        new RtSymbol(bf.Field.Key.Name),
                        EvalFieldValue(bf.Field.Value, env),
                    }));
                    break;
            }
        }
        return new RtSet(elements);
    }

    private RtValue EvalFieldValue(AstFieldValue v, RtEnv env)
    {
        return v switch
        {
            AstFieldExpr fe => Eval(fe.Value, env),
            AstFieldTyped ft => Eval(ft.Value, env),
            AstFieldBlock fb => EvalBlockAsSet(fb.Block, env),
            _ => new RtVoid(),
        };
    }

    private RtValue EvalBlockAsSet(AstBlock block, RtEnv env)
    {
        var elements = new List<RtValue>();
        foreach (var f in block.Fields)
        {
            elements.Add(new RtTuple(new RtValue[]
            {
                new RtSymbol(f.Key.Name),
                EvalFieldValue(f.Value, env),
            }));
        }
        return new RtSet(elements);
    }

    private RtValue EvalRange(AstRangeLit rl, RtEnv env)
    {
        int start = AsInt(Eval(rl.Start, env));
        int end = AsInt(Eval(rl.End, env));
        var xs = new List<RtValue>(Math.Max(0, end - start + 1));
        for (int i = start; i <= end; i++) xs.Add(new RtInt(i));
        return new RtList(xs);
    }

    private RtValue EvalSwitch(AstSwitchExpr sw, RtEnv env)
    {
        var scrutinee = Eval(sw.Scrutinee, env);
        foreach (var c in sw.Cases)
        {
            if (c.Label == "Default") return Eval(c.Value, env);
            if (scrutinee is RtSymbol s && s.Name == c.Label) return Eval(c.Value, env);
        }
        return new RtVoid();
    }

    private RtValue EvalCond(AstCondExpr co, RtEnv env)
    {
        foreach (var arm in co.Arms)
        {
            if (arm.Predicate is AstIdent { Name: "Default" })
            {
                return Eval(arm.Effect, env);
            }
            if (AsBool(Eval(arm.Predicate, env)))
            {
                return Eval(arm.Effect, env);
            }
        }
        return new RtVoid();
    }

    // -------------------------------------------------------------------------
    // Coercions and value helpers
    // -------------------------------------------------------------------------

    internal static int AsInt(RtValue v) => v switch
    {
        RtInt i => i.V,
        RtBool b => b.V ? 1 : 0,
        _ => 0,
    };

    internal static bool AsBool(RtValue v) => v switch
    {
        RtBool b => b.V,
        RtInt i => i.V != 0,
        RtNone => false,
        RtUnbound => false,
        _ => true,
    };

    internal static IReadOnlyList<RtValue> AsList(RtValue v) => v switch
    {
        RtList l => l.Elements,
        RtSet s => s.Elements,
        _ => Array.Empty<RtValue>(),
    };

    internal static bool RtEquals(RtValue a, RtValue b) => (a, b) switch
    {
        (RtInt ai, RtInt bi) => ai.V == bi.V,
        (RtString sa, RtString sb) => sa.V == sb.V,
        (RtBool ba, RtBool bb) => ba.V == bb.V,
        (RtSymbol sa, RtSymbol sb) => sa.Name == sb.Name,
        (RtEntityRef ea, RtEntityRef eb) => ea.Id == eb.Id,
        (RtNone, RtNone) => true,
        (RtUnbound, RtUnbound) => true,
        _ => false,
    };

    private static bool SetContains(RtValue setValue, RtValue needle) =>
        AsList(setValue).Any(e => RtEquals(e, needle));

    private static bool IsSubset(RtValue a, RtValue b)
    {
        var big = AsList(b);
        foreach (var x in AsList(a))
        {
            if (!big.Any(y => RtEquals(x, y))) return false;
        }
        return true;
    }

    private static RtValue CartesianProduct(RtValue a, RtValue b)
    {
        var result = new List<RtValue>();
        foreach (var x in AsList(a))
        foreach (var y in AsList(b))
        {
            result.Add(new RtTuple(new[] { x, y }));
        }
        return new RtSet(result);
    }

    private static RtValue Intersect(RtValue a, RtValue b)
    {
        var bl = AsList(b);
        var result = new List<RtValue>();
        foreach (var x in AsList(a))
        {
            if (bl.Any(y => RtEquals(x, y))) result.Add(x);
        }
        return new RtSet(result);
    }
}

/// <summary>
/// Value-form of an <c>Event.Foo(...)</c> expression before it is emitted onto
/// the queue. Kept internal to the interpreter — <see cref="GameEvent"/> is
/// the dispatch-side object; <see cref="RtEventLit"/> exists only so
/// <c>EmitEvent(Event.X(...))</c> can evaluate its argument once and then
/// hand a clean payload to the scheduler.
/// </summary>
public sealed record RtEventLit(string TypeName, IReadOnlyDictionary<string, RtValue> Fields) : RtValue
{
    public override string ToString() =>
        Fields.Count == 0 ? $"Event.{TypeName}" : $"Event.{TypeName}({string.Join(", ", Fields.Select(kv => $"{kv.Key}={kv.Value}"))})";
}
