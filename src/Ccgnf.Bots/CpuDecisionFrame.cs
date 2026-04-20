using System.Text.Json;
using System.Text.Json.Serialization;
using Ccgnf.Bots.Utility;
using Ccgnf.Interpreter;

namespace Ccgnf.Bots;

/// <summary>
/// Shape the bot emits per decision for debugging + replay. Serialises
/// to JSON for SSE frames (via <c>EventType: "CpuDecision"</c>) and for
/// the JSONL decision log. See §10.2f in the plan.
/// </summary>
public sealed record CpuDecisionFrame
{
    public string Intent { get; init; } = "";
    public string ChosenKind { get; init; } = "";
    public string ChosenLabel { get; init; } = "";
    public float ChosenScore { get; init; }
    public IReadOnlyList<ScoredActionRecord> Top { get; init; } = Array.Empty<ScoredActionRecord>();
    public long StepCount { get; init; }
    public string Prompt { get; init; } = "";
    public int CpuEntityId { get; init; }
    public string Timestamp { get; init; } = "";
}

/// <summary>
/// One scored action row — subset of <see cref="ScoredAction"/> that
/// renders cleanly into JSON (no <see cref="LegalAction"/> metadata
/// references that would complicate serialisation).
/// </summary>
public sealed record ScoredActionRecord(
    string Kind,
    string Label,
    float Score,
    IReadOnlyDictionary<string, float> Breakdown);

public static class CpuDecisionRecorder
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Compose a frame from a ranked score list + the pick. <paramref name="topN"/>
    /// caps how many rows ride along for the debug overlay.
    /// </summary>
    public static CpuDecisionFrame Build(
        Intent intent,
        IReadOnlyList<ScoredAction> ranked,
        LegalAction chosen,
        float chosenScore,
        long stepCount,
        string prompt,
        int cpuEntityId,
        int topN = 3)
    {
        var top = ranked.Take(topN).Select(r => new ScoredActionRecord(
            r.Action.Kind,
            r.Action.Label,
            r.Score,
            r.Breakdown)).ToArray();

        return new CpuDecisionFrame
        {
            Intent = intent.ToString(),
            ChosenKind = chosen.Kind,
            ChosenLabel = chosen.Label,
            ChosenScore = chosenScore,
            Top = top,
            StepCount = stepCount,
            Prompt = prompt,
            CpuEntityId = cpuEntityId,
            Timestamp = DateTime.UtcNow.ToString("o"),
        };
    }

    /// <summary>
    /// Append one frame to <paramref name="path"/> as a JSONL line.
    /// Parent directory is created on demand. Matches
    /// <c>reference-code/BehaviorTree/TreeLog.cs</c>'s append contract.
    /// </summary>
    public static void AppendJsonl(string path, CpuDecisionFrame frame)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.AppendAllText(path, JsonSerializer.Serialize(frame, _opts) + "\n");
    }

    /// <summary>
    /// Serialise one frame for SSE embedding. Returns a flat string dict
    /// so the existing <c>RoomEventFrame.Fields</c> shape fits.
    /// </summary>
    public static Dictionary<string, string> ToFields(CpuDecisionFrame frame) => new()
    {
        ["intent"] = frame.Intent,
        ["chosenKind"] = frame.ChosenKind,
        ["chosenLabel"] = frame.ChosenLabel,
        ["chosenScore"] = frame.ChosenScore.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
        ["top"] = JsonSerializer.Serialize(frame.Top, _opts),
        ["stepCount"] = frame.StepCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["prompt"] = frame.Prompt,
        ["cpuEntityId"] = frame.CpuEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public static string SerializeFrame(CpuDecisionFrame frame) =>
        JsonSerializer.Serialize(frame, _opts);
}
