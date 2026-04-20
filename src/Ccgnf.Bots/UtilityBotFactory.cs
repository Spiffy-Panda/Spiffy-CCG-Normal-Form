using Ccgnf.Bots.Bt;
using Ccgnf.Bots.Utility;
using Ccgnf.Interpreter;

namespace Ccgnf.Bots;

/// <summary>
/// Composes a ready-to-run <see cref="UtilityBot"/> from the default
/// consideration set, the default phase-BT, and (optionally) a live
/// <see cref="PhaseMemory"/> instance for sticky intent.
/// <para>
/// Used by Room construction and by the benchmark harness so both paths
/// share the same wiring. The factory doesn't own any state — it just
/// snaps the pieces together.
/// </para>
/// </summary>
public static class UtilityBotFactory
{
    /// <summary>
    /// Build a utility bot with the supplied weights, sticky memory, and
    /// decision observer. Any argument may be null; sensible defaults
    /// are substituted.
    /// </summary>
    public static UtilityBot Build(
        WeightTable? weights = null,
        PhaseMemory? memory = null,
        Action<CpuDecisionFrame>? onDecision = null,
        IReadOnlyList<BtNode>? phaseBt = null,
        IRoomBot? fallback = null)
    {
        var considerations = DefaultConsiderations.All();
        var selector = new PhaseBtIntentSelector(phaseBt ?? DefaultPhaseBt.Build());

        UtilityBot.IntentSelector intentSelector = memory is null
            ? (state, pending, cpuId) => selector.Select(state, pending, cpuId)
            : (state, pending, cpuId) =>
                {
                    var intent = selector.SelectWithMemory(state, pending, cpuId, memory);
                    var phase = ReadPhaseName(state);
                    memory.OnDecisionRecorded(intent, phase);
                    return intent;
                };

        var bot = new UtilityBot(
            considerations,
            weights ?? WeightTable.Uniform(considerations.Select(c => c.Key)),
            fallback,
            intentSelector);
        if (onDecision is not null) bot.OnDecision = onDecision;
        return bot;
    }

    /// <summary>
    /// Reads the current phase name off the Game entity. Returns "" when
    /// the encoding doesn't expose one (tests, pre-init states).
    /// </summary>
    internal static string ReadPhaseName(GameState state)
    {
        if (state.Game is null) return "";
        if (!state.Game.Characteristics.TryGetValue("phase", out var v)) return "";
        return v switch
        {
            RtSymbol s => s.Name,
            RtString str => str.V,
            _ => v.ToString() ?? "",
        };
    }
}
