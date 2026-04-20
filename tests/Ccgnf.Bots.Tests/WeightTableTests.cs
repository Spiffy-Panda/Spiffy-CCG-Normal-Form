namespace Ccgnf.Bots.Tests;

public class WeightTableTests
{
    [Fact]
    public void MissingEntriesReturnZero()
    {
        var table = new WeightTable(new Dictionary<Intent, Dictionary<string, float>>());
        Assert.Equal(0f, table.Get(Intent.Default, "on_curve"));
    }

    [Fact]
    public void UniformTableReturnsConstant()
    {
        var table = WeightTable.Uniform(new[] { "on_curve", "tempo_per_aether" }, 1.0f);
        Assert.Equal(1.0f, table.Get(Intent.Default, "on_curve"));
        Assert.Equal(1.0f, table.Get(Intent.EarlyTempo, "tempo_per_aether"));
        Assert.Equal(0f, table.Get(Intent.Default, "not_registered"));
    }

    [Fact]
    public void FromJsonParsesIntentBlocks()
    {
        const string json = """
        {
          "version": 1,
          "intents": {
            "default": { "on_curve": 1.0 },
            "early_tempo": { "on_curve": 1.4, "tempo_per_aether": 1.0 },
            "pushing": { "resonance_alignment": 2.2 }
          }
        }
        """;
        var table = WeightTable.FromJson(json);
        Assert.Equal(1.0f, table.Get(Intent.Default, "on_curve"));
        Assert.Equal(1.4f, table.Get(Intent.EarlyTempo, "on_curve"));
        Assert.Equal(1.0f, table.Get(Intent.EarlyTempo, "tempo_per_aether"));
        Assert.Equal(2.2f, table.Get(Intent.Pushing, "resonance_alignment"));
        // Unlisted intents → 0
        Assert.Equal(0f, table.Get(Intent.DefendConduit, "on_curve"));
        // Unlisted considerations within a listed intent → 0
        Assert.Equal(0f, table.Get(Intent.Pushing, "on_curve"));
    }

    [Fact]
    public void FromJsonRejectsWrongVersion()
    {
        const string json = """{"version": 2, "intents": {}}""";
        Assert.Throws<WeightTableFormatException>(() => WeightTable.FromJson(json));
    }

    [Fact]
    public void FromJsonRejectsUnknownIntent()
    {
        const string json = """{"version": 1, "intents": {"not_an_intent": {"on_curve": 1}}}""";
        Assert.Throws<WeightTableFormatException>(() => WeightTable.FromJson(json));
    }

    [Fact]
    public void FromJsonRejectsMalformedDocument()
    {
        Assert.Throws<WeightTableFormatException>(() => WeightTable.FromJson("{not-json"));
    }

    [Fact]
    public void FromJsonAcceptsUnknownConsiderationKeys()
    {
        // Forward-compatibility: a weights file that references a
        // consideration this build doesn't know about should parse fine.
        const string json = """
        {
          "version": 1,
          "intents": { "default": { "future_consideration": 1.0, "on_curve": 2.0 } }
        }
        """;
        var table = WeightTable.FromJson(json);
        Assert.Equal(1.0f, table.Get(Intent.Default, "future_consideration"));
        Assert.Equal(2.0f, table.Get(Intent.Default, "on_curve"));
    }
}
