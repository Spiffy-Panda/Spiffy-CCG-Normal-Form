using Ccgnf.Ast;

namespace Ccgnf.Interpreter;

/// <summary>
/// Mutable world state. Owns all entities keyed by id. The singleton
/// <see cref="Game"/>, the two <see cref="Players"/>, and the three
/// <see cref="Arenas"/> are cached for O(1) access; everything else (Conduits,
/// PlayBindings, Cards in zones) is reachable via <see cref="Entities"/>.
/// <para>
/// Globals holds runtime-only values not attached to an entity — a symbol
/// table the interpreter populates and reads during effect evaluation.
/// </para>
/// </summary>
public sealed class GameState
{
    private int _nextEntityId = 1;

    public Dictionary<int, Entity> Entities { get; } = new();

    public Entity Game { get; set; } = null!;
    public List<Entity> Players { get; } = new();
    public List<Entity> Arenas { get; } = new();

    /// <summary>
    /// Name-keyed lookup for entities addressable by name in the encoding —
    /// Player1, Player2, ArenaLeft, etc. Populated by the state builder.
    /// </summary>
    public Dictionary<string, Entity> NamedEntities { get; } = new(StringComparer.Ordinal);

    public EventQueue PendingEvents { get; } = new();

    /// <summary>
    /// Card declarations from the loaded <see cref="AstFile"/>, keyed by
    /// <see cref="AstCardDecl.Name"/>. The runtime looks these up when a
    /// <see cref="Entity"/> of kind <c>Card</c> is played so it can find the
    /// card's cost and <c>OnResolve</c> effect without re-walking the AST
    /// each time.
    /// </summary>
    public Dictionary<string, AstCardDecl> CardDecls { get; } = new(StringComparer.Ordinal);

    /// <summary>Increments on each event dequeued from the main loop.</summary>
    public long StepCount { get; set; }

    /// <summary>Terminal flag — set by the event loop when a GameEnd event is committed.</summary>
    public bool GameOver { get; set; }

    public Entity AllocateEntity(string kind, string displayName)
    {
        var entity = new Entity(_nextEntityId++, kind, displayName);
        Entities[entity.Id] = entity;
        return entity;
    }
}
