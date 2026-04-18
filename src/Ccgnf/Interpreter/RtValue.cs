using Ccgnf.Ast;

namespace Ccgnf.Interpreter;

// -----------------------------------------------------------------------------
// Runtime value representation for the v1 interpreter.
//
// Expressions from the AST are evaluated into RtValues. Everything at runtime
// is one of: primitive literal, symbol (enum-like identifier), set/list/tuple,
// entity or zone reference, a captured lambda, or a couple of sentinel forms
// (None, Unbound, NoOp, Void for effects that return nothing meaningful).
// Complex game objects (Player, Arena, Conduit) live in GameState; the
// RtEntityRef is just their id.
// -----------------------------------------------------------------------------

public abstract record RtValue;

public sealed record RtInt(int V) : RtValue { public override string ToString() => V.ToString(); }

public sealed record RtString(string V) : RtValue { public override string ToString() => "\"" + V + "\""; }

public sealed record RtBool(bool V) : RtValue { public override string ToString() => V ? "true" : "false"; }

/// <summary>
/// An enum-like identifier — Left, Center, Right, EMBER, Rise, Unit, etc.
/// Distinct from RtString: symbols are printable as bare words and compared
/// by identity, not string contents.
/// </summary>
public sealed record RtSymbol(string Name) : RtValue { public override string ToString() => Name; }

public sealed record RtSet(IReadOnlyList<RtValue> Elements) : RtValue
{
    public override string ToString() => "{" + string.Join(", ", Elements) + "}";
}

public sealed record RtList(IReadOnlyList<RtValue> Elements) : RtValue
{
    public override string ToString() => "[" + string.Join(", ", Elements) + "]";
}

public sealed record RtTuple(IReadOnlyList<RtValue> Elements) : RtValue
{
    public override string ToString() => "(" + string.Join(", ", Elements) + ")";
}

/// <summary>Reference to an entity by its integer id.</summary>
public sealed record RtEntityRef(int Id) : RtValue { public override string ToString() => $"#{Id}"; }

/// <summary>Reference to a named zone on an owning entity.</summary>
public sealed record RtZoneRef(int OwnerId, string Name) : RtValue
{
    public override string ToString() => $"#{OwnerId}.{Name}";
}

public sealed record RtLambda(IReadOnlyList<string> Parameters, AstExpr Body) : RtValue;

/// <summary>Sentinel — None, used for characteristic defaults.</summary>
public sealed record RtNone : RtValue { public override string ToString() => "None"; }

/// <summary>Sentinel — Unbound, a not-yet-set characteristic.</summary>
public sealed record RtUnbound : RtValue { public override string ToString() => "Unbound"; }

/// <summary>Sentinel — NoOp effect; see §0 schema.</summary>
public sealed record RtNoOp : RtValue { public override string ToString() => "NoOp"; }

/// <summary>Sentinel — the value of an effect-producing expression.</summary>
public sealed record RtVoid : RtValue { public override string ToString() => "()"; }
