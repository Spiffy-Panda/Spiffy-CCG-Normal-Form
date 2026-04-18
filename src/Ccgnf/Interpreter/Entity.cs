namespace Ccgnf.Interpreter;

/// <summary>
/// A live entity in the game — a Game, Player, Arena, Conduit, Card, Token,
/// or PlayBinding. Fields are mutable: the interpreter modifies counters,
/// characteristics, and zone contents during effect evaluation.
/// <para>
/// The AST declaration this entity was instantiated from is not referenced
/// here; abilities attached to the entity are stored separately via
/// <see cref="AbilityInstance"/>. An entity's identity is its integer Id.
/// </para>
/// </summary>
public sealed class Entity
{
    public int Id { get; }
    public string Kind { get; }
    public string DisplayName { get; }

    /// <summary>
    /// Characteristics declared in the entity's body. Mutable — Static
    /// modifiers and one-shot effects may rewrite these at layer 2/3 per
    /// <c>encoding/common/01-schema.ccgnf</c>. v1 treats them as raw storage.
    /// </summary>
    public Dictionary<string, RtValue> Characteristics { get; } = new();

    public Dictionary<string, int> Counters { get; } = new();

    public HashSet<string> Tags { get; } = new();

    /// <summary>
    /// Named zones owned by this entity (Player.Arsenal, Player.Hand, etc.).
    /// Empty for entities that don't own zones.
    /// </summary>
    public Dictionary<string, Zone> Zones { get; } = new();

    /// <summary>
    /// Parameters bound when the entity was instantiated from a parameterized
    /// template — e.g., a Conduit carries <c>owner</c> and <c>arena</c>.
    /// </summary>
    public Dictionary<string, RtValue> Parameters { get; } = new();

    public List<AbilityInstance> Abilities { get; } = new();

    public int? OwnerId { get; set; }

    public Entity(int id, string kind, string displayName)
    {
        Id = id;
        Kind = kind;
        DisplayName = displayName;
    }
}
