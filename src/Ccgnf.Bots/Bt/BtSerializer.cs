using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ccgnf.Bots.Bt;

/// <summary>
/// JSON round-trip for the phase-BT. Ported from
/// <c>reference-code/BehaviorTree/BtSerializer.cs</c>. The file format is
/// a JSON array of <see cref="BtNode"/> root objects so a future BT can
/// grow multiple parallel roots without a format migration.
/// </summary>
public static class BtSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IReadOnlyList<BtNode> roots) =>
        JsonSerializer.Serialize(roots, Options);

    public static List<BtNode> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<BtNode>>(json, Options) ?? new List<BtNode>();
        }
        catch (JsonException ex)
        {
            throw new BtFormatException("malformed BT JSON", ex);
        }
    }
}

public sealed class BtFormatException : Exception
{
    public BtFormatException(string message) : base(message) { }
    public BtFormatException(string message, Exception inner) : base(message, inner) { }
}
