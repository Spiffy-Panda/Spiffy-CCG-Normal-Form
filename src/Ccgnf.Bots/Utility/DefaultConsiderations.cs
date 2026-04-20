using Ccgnf.Bots.Utility.Considerations;

namespace Ccgnf.Bots.Utility;

/// <summary>
/// Canonical consideration set shipped with the bot. One function so the
/// ordering + membership stays consistent across the Room wiring, the
/// benchmark harness, and the "preview scoring" surface the
/// <c>#/ai</c> editor calls into.
/// </summary>
public static class DefaultConsiderations
{
    public static IReadOnlyList<IConsideration> All() => new IConsideration[]
    {
        new OnCurveConsideration(),
        new TempoPerAetherConsideration(),
        new LowestLiveHpConsideration(),
        new OpponentPriorityConsideration(),
        new OverlapConsideration(),
        new ConduitSoftnessConsideration(),
        new ThreatAvoidanceConsideration(),
    };

    /// <summary>Consideration keys in declaration order.</summary>
    public static IReadOnlyList<string> Keys() => All().Select(c => c.Key).ToArray();
}
