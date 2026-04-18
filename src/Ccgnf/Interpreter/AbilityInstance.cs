using Ccgnf.Ast;

namespace Ccgnf.Interpreter;

public enum AbilityKind
{
    Static,
    Triggered,
    OnResolve,
    Replacement,
    Activated,
}

/// <summary>
/// An ability attached to an entity. Parsed lazily from its source call site
/// (e.g., <c>Triggered(on: X, effect: Y)</c>) — the named args are cached in
/// <see cref="Named"/> so the event dispatcher can pattern-match without
/// re-walking the AST on every event.
/// <para>
/// v1 only instantiates Triggered abilities; Static/OnResolve/Replacement/
/// Activated are parsed but not dispatched.
/// </para>
/// </summary>
public sealed class AbilityInstance
{
    public AbilityKind Kind { get; }
    public int OwnerId { get; }
    public IReadOnlyDictionary<string, AstExpr> Named { get; }
    public IReadOnlyList<AstExpr> Positional { get; }

    public AbilityInstance(
        AbilityKind kind,
        int ownerId,
        IReadOnlyDictionary<string, AstExpr> named,
        IReadOnlyList<AstExpr> positional)
    {
        Kind = kind;
        OwnerId = ownerId;
        Named = named;
        Positional = positional;
    }

    public AstExpr? OnPattern => Named.TryGetValue("on", out var v) ? v : null;
    public AstExpr? Effect => Named.TryGetValue("effect", out var v) ? v : null;
    public AstExpr? Rule => Named.TryGetValue("rule", out var v) ? v : null;

    public int UsedThisTurn { get; set; }
}
