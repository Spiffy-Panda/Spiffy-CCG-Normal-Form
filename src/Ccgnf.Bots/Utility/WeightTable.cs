using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ccgnf.Bots.Utility;

/// <summary>
/// Per-intent weight lookup. A missing (intent, key) entry returns
/// <c>0</c> — an unspecified consideration is off, not average (see
/// 10.2c in <c>docs/plan/steps/10.2-long-term-ai-plan.md</c>).
/// </summary>
public sealed class WeightTable
{
    private readonly Dictionary<Intent, Dictionary<string, float>> _weights;

    public WeightTable(Dictionary<Intent, Dictionary<string, float>> weights)
    {
        _weights = weights ?? throw new ArgumentNullException(nameof(weights));
    }

    /// <summary>
    /// Look up the weight for <paramref name="consideration"/> under
    /// <paramref name="intent"/>. Unlisted entries return 0.
    /// </summary>
    public float Get(Intent intent, string consideration)
    {
        if (_weights.TryGetValue(intent, out var inner)
            && inner.TryGetValue(consideration, out var w))
        {
            return w;
        }
        return 0f;
    }

    /// <summary>
    /// A safe default: every consideration at 1.0 under every intent.
    /// Used when no weights file is present. The UtilityBot is still
    /// meaningful because the considerations themselves already encode
    /// the scoring curves.
    /// </summary>
    public static WeightTable Uniform(IEnumerable<string> considerationKeys, float value = 1.0f)
    {
        var dict = new Dictionary<Intent, Dictionary<string, float>>();
        foreach (Intent intent in Enum.GetValues(typeof(Intent)))
        {
            var inner = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var k in considerationKeys) inner[k] = value;
            dict[intent] = inner;
        }
        return new WeightTable(dict);
    }

    /// <summary>
    /// Parse a <see cref="WeightTable"/> from the <c>utility-weights.json</c>
    /// schema (see 10.2c in the plan).
    /// <para>Expected shape:</para>
    /// <code>
    /// { "version": 1, "intents": {
    ///     "early_tempo":    { "on_curve": 1.4, "tempo_per_aether": 1.0, ... },
    ///     "pushing":        { ... },
    ///     ...
    /// }}
    /// </code>
    /// Unknown intent keys raise <see cref="WeightTableFormatException"/>;
    /// unknown consideration keys are accepted silently so the scorer
    /// can stay forward-compatible with new considerations.
    /// </summary>
    public static WeightTable FromJson(string json)
    {
        WeightFile? file;
        try
        {
            file = JsonSerializer.Deserialize<WeightFile>(json, _opts);
        }
        catch (JsonException ex)
        {
            throw new WeightTableFormatException("malformed JSON", ex);
        }

        if (file is null)
            throw new WeightTableFormatException("empty document");
        if (file.Version != 1)
            throw new WeightTableFormatException($"unsupported version {file.Version} (expected 1)");
        if (file.Intents is null)
            throw new WeightTableFormatException("missing 'intents' block");

        var table = new Dictionary<Intent, Dictionary<string, float>>();
        foreach (var (intentName, inner) in file.Intents)
        {
            if (!TryParseIntent(intentName, out var intent))
                throw new WeightTableFormatException($"unknown intent '{intentName}'");
            table[intent] = new Dictionary<string, float>(inner, StringComparer.Ordinal);
        }
        return new WeightTable(table);
    }

    private static bool TryParseIntent(string s, out Intent intent)
    {
        var normalised = s.Replace("_", "", StringComparison.Ordinal);
        return Enum.TryParse(normalised, ignoreCase: true, out intent);
    }

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class WeightFile
    {
        public int Version { get; set; }
        public Dictionary<string, Dictionary<string, float>>? Intents { get; set; }
    }
}

/// <summary>Raised by <see cref="WeightTable.FromJson"/> for malformed input.</summary>
public sealed class WeightTableFormatException : Exception
{
    public WeightTableFormatException(string message) : base(message) { }
    public WeightTableFormatException(string message, Exception inner) : base(message, inner) { }
}
