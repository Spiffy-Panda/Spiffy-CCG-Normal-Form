using Ccgnf.Interpreter;

namespace Ccgnf.Rest.Serialization;

/// <summary>
/// JSON-friendly projection of <see cref="GameState"/>. Lossy by design — the
/// REST surface publishes what a consumer needs to render a state view, not
/// the full interpreter-internal detail (no AST nodes, no logger references).
/// </summary>
public sealed record GameStateDto(
    long StepCount,
    bool GameOver,
    int GameId,
    IReadOnlyList<int> PlayerIds,
    IReadOnlyList<int> ArenaIds,
    IReadOnlyList<EntityDto> Entities,
    IReadOnlyList<EventDto> Pending);

public sealed record EntityDto(
    int Id,
    string Kind,
    string DisplayName,
    int? OwnerId,
    IReadOnlyDictionary<string, string> Characteristics,
    IReadOnlyDictionary<string, int> Counters,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyDictionary<string, ZoneDto> Zones,
    IReadOnlyList<string> Tags,
    int AbilityCount);

public sealed record ZoneDto(
    string Order,
    int? Capacity,
    IReadOnlyList<int> Contents);

public sealed record EventDto(
    string Type,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// Maps the in-memory <see cref="GameState"/> onto its DTO form. Kept a plain
/// static utility — the shape is narrow and there's no polymorphism to
/// warrant an abstraction.
/// </summary>
public static class StateMapper
{
    public static GameStateDto ToDto(GameState state) => new(
        StepCount: state.StepCount,
        GameOver: state.GameOver,
        GameId: state.Game?.Id ?? 0,
        PlayerIds: state.Players.Select(p => p.Id).ToList(),
        ArenaIds: state.Arenas.Select(a => a.Id).ToList(),
        Entities: state.Entities.Values
            .OrderBy(e => e.Id)
            .Select(ToEntityDto)
            .ToList(),
        Pending: state.PendingEvents.Snapshot().Select(ToEventDto).ToList());

    public static EntityDto ToEntityDto(Entity e) => new(
        Id: e.Id,
        Kind: e.Kind,
        DisplayName: e.DisplayName,
        OwnerId: e.OwnerId,
        Characteristics: e.Characteristics
            .ToDictionary(kv => kv.Key, kv => Format(kv.Value)),
        Counters: new Dictionary<string, int>(e.Counters),
        Parameters: e.Parameters
            .ToDictionary(kv => kv.Key, kv => Format(kv.Value)),
        Zones: e.Zones.ToDictionary(
            kv => kv.Key,
            kv => new ZoneDto(
                Order: kv.Value.Order.ToString(),
                Capacity: kv.Value.Capacity,
                Contents: new List<int>(kv.Value.Contents))),
        Tags: e.Tags.ToList(),
        AbilityCount: e.Abilities.Count);

    public static EventDto ToEventDto(GameEvent ev) => new(
        Type: ev.TypeName,
        Fields: ev.Fields.ToDictionary(kv => kv.Key, kv => Format(kv.Value)));

    private static string Format(RtValue v) => v switch
    {
        RtInt i => i.V.ToString(),
        RtString s => s.V,
        RtBool b => b.V ? "true" : "false",
        RtSymbol s => s.Name,
        RtEntityRef er => $"#{er.Id}",
        RtZoneRef zr => $"#{zr.OwnerId}.{zr.Name}",
        RtNone => "None",
        RtUnbound => "Unbound",
        _ => v.ToString() ?? "",
    };
}
