using Ccgnf.Bots.Utility;
using Ccgnf.Interpreter;

namespace Ccgnf.Bots.Bt;

/// <summary>
/// Bridges a loaded <see cref="BtRunner"/> to <see cref="UtilityBot"/>'s
/// <see cref="UtilityBot.IntentSelector"/>. Each invocation builds a
/// fresh <see cref="PhaseBtContext"/>, runs the tree once, and returns
/// the chosen intent.
/// <para>
/// When a <see cref="PhaseMemory"/> is supplied, stickiness biases the
/// BT toward the previous intent for up to <c>StickyMaxAge</c> decisions
/// (see 10.2g). The bias is applied by short-circuiting: if
/// <see cref="PhaseMemory.CurrentBias"/> &gt; 1 and the previous intent
/// is one of the candidates the BT would pick under a relaxed gate, we
/// return it without re-running the full tree. Lethal-check and
/// defend-conduit gates always re-evaluate — safety intents should
/// never be skipped.
/// </para>
/// </summary>
public sealed class PhaseBtIntentSelector
{
    private readonly BtRunner _runner;

    public PhaseBtIntentSelector(IReadOnlyList<BtNode> roots)
    {
        _runner = new BtRunner(roots);
    }

    public static PhaseBtIntentSelector Default() => new(DefaultPhaseBt.Build());

    /// <summary>
    /// Adapter matching <see cref="UtilityBot.IntentSelector"/>.
    /// </summary>
    public Intent Select(GameState state, InputRequest pending, int cpuEntityId)
    {
        _ = pending;
        var ctx = new PhaseBtContext(state, cpuEntityId);
        _runner.Apply(ctx);
        return ctx.ChosenIntent;
    }

    /// <summary>
    /// Memory-aware variant of <see cref="Select"/>. Always runs the BT
    /// first. Then: if the BT picked a safety intent
    /// (<see cref="Intent.LethalCheck"/> or <see cref="Intent.DefendConduit"/>),
    /// that always wins — stickiness is overridden. Otherwise, if
    /// stickiness is active (<see cref="PhaseMemory.CurrentBias"/> &gt; 1),
    /// the previously-chosen intent is kept.
    /// </summary>
    public Intent SelectWithMemory(
        GameState state, InputRequest pending, int cpuEntityId, PhaseMemory memory)
    {
        var fresh = Select(state, pending, cpuEntityId);

        // Safety intents short-circuit stickiness — if the BT says
        // "defend" or "lethal", we listen no matter what memory says.
        if (fresh is Intent.LethalCheck or Intent.DefendConduit)
            return fresh;

        if (memory.CurrentBias > 1.0f && memory.LastIntent != Intent.Default)
            return memory.LastIntent;

        return fresh;
    }
}
