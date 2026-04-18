namespace Ccgnf.Interpreter;

/// <summary>
/// Runtime event emitted by game logic. The <see cref="TypeName"/> is the
/// short tag (e.g., "GameStart", "PhaseBegin", "FirstPlayerChosen") and
/// <see cref="Fields"/> carries the named payload values extracted from the
/// emit site (phase: Rise, player: Player1, ...).
/// </summary>
public sealed class GameEvent
{
    public string TypeName { get; }
    public IReadOnlyDictionary<string, RtValue> Fields { get; }

    public GameEvent(string typeName, IReadOnlyDictionary<string, RtValue> fields)
    {
        TypeName = typeName;
        Fields = fields;
    }

    public override string ToString()
    {
        if (Fields.Count == 0) return "Event." + TypeName;
        var parts = Fields.Select(kv => $"{kv.Key}={kv.Value}");
        return $"Event.{TypeName}({string.Join(", ", parts)})";
    }
}
