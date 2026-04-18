namespace Ccgnf.Interpreter;

public enum ZoneOrder
{
    Unordered,
    Sequential,
    FIFO,
    LIFO,
}

/// <summary>
/// Ordered (or unordered) bag of entity ids. The v1 interpreter uses zones
/// for Arsenal / Hand / Cache / ResonanceField / Void, plus per-Arena lanes.
/// <para>
/// Zones are addressed by their owning entity's id and their name; mutations
/// go through the owning entity's <c>Zones</c> dictionary so state stays
/// coherent for serialization.
/// </para>
/// </summary>
public sealed class Zone
{
    public string Name { get; }
    public ZoneOrder Order { get; }
    public int? Capacity { get; }
    public List<int> Contents { get; } = new();

    public Zone(string name, ZoneOrder order = ZoneOrder.Unordered, int? capacity = null)
    {
        Name = name;
        Order = order;
        Capacity = capacity;
    }

    public int Count => Contents.Count;
}
