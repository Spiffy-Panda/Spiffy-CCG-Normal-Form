using Ccgnf.Interpreter;

namespace Ccgnf.Bots.Utility;

/// <summary>
/// Utility-based CPU bot. For each <see cref="LegalAction"/> in the
/// pending <see cref="InputRequest"/>, sums the weighted outputs of every
/// <see cref="IConsideration"/> that <see cref="IConsideration.Handles"/>
/// its <see cref="LegalAction.Kind"/>. Picks the action with the highest
/// total score. Ties are broken deterministically via a seed derived from
/// the room state so replays reproduce exactly.
/// <para>
/// Actions that no consideration handles (e.g. <c>pass_priority</c> in
/// the current consideration set) score 0; when the top score is 0 — no
/// consideration spoke up — the bot falls through to the supplied
/// <see cref="IRoomBot"/> fallback (default: <see cref="FixedLadderBot"/>).
/// This keeps behaviour covered during the incremental rollout through
/// 10.2c, 10.2d, and onward.
/// </para>
/// </summary>
public sealed class UtilityBot : IRoomBot
{
    private readonly IReadOnlyList<IConsideration> _considerations;
    private readonly WeightTable _weights;
    private readonly IRoomBot _fallback;
    private readonly IntentSelector _selectIntent;

    /// <summary>
    /// Optional callback fired after each <see cref="Choose"/> call with
    /// the composed <see cref="CpuDecisionFrame"/>. Used by the Room host
    /// to emit an SSE <c>CpuDecision</c> event + optionally append to the
    /// JSONL log when <c>CCGNF_AI_DEBUG=1</c>. Nulls out when not set.
    /// </summary>
    public Action<CpuDecisionFrame>? OnDecision { get; set; }

    /// <summary>
    /// Delegate that chooses which <see cref="Intent"/> applies to the
    /// current decision. In 10.2c it's always <see cref="Intent.Default"/>;
    /// the real phase-BT lands in 10.2e and swaps this out.
    /// </summary>
    public delegate Intent IntentSelector(GameState state, InputRequest pending, int cpuEntityId);

    public UtilityBot(
        IEnumerable<IConsideration> considerations,
        WeightTable? weights = null,
        IRoomBot? fallback = null,
        IntentSelector? selectIntent = null)
    {
        _considerations = considerations.ToArray();
        _weights = weights ?? WeightTable.Uniform(_considerations.Select(c => c.Key));
        _fallback = fallback ?? new FixedLadderBot();
        _selectIntent = selectIntent ?? ((_, _, _) => Intent.Default);
    }

    public RtValue Choose(GameState state, InputRequest pending, int cpuEntityId)
    {
        var actions = pending.LegalActions;
        if (actions.Count == 0) return new RtSymbol("pass");

        var intent = _selectIntent(state, pending, cpuEntityId);
        var ctx = new ScoringContext(state, pending, cpuEntityId, intent);

        float bestScore = float.NegativeInfinity;
        LegalAction? best = null;
        int bestTieBreak = 0;

        foreach (var action in actions)
        {
            float score = ScoreAction(ctx, action);
            int tieBreak = TieBreakHash(state, pending, action);

            if (score > bestScore || (score == bestScore && tieBreak < bestTieBreak))
            {
                bestScore = score;
                best = action;
                bestTieBreak = tieBreak;
            }
        }

        // If nothing scored above zero, no consideration had an opinion —
        // delegate to the fallback ladder (§10.2b). This is the property
        // that keeps behaviour safe as new considerations land one at a
        // time: an unhandled LegalAction.Kind always gets the ladder's pick.
        if (bestScore <= 0f || best is null)
            return _fallback.Choose(state, pending, cpuEntityId);

        if (OnDecision is { } cb)
        {
            var ranked = BuildRanked(state, pending, cpuEntityId, intent);
            cb(CpuDecisionRecorder.Build(
                intent: intent,
                ranked: ranked,
                chosen: best,
                chosenScore: bestScore,
                stepCount: state.StepCount,
                prompt: pending.Prompt,
                cpuEntityId: cpuEntityId));
        }

        return new RtSymbol(best.Label);
    }

    private IReadOnlyList<ScoredAction> BuildRanked(
        GameState state, InputRequest pending, int cpuEntityId, Intent intent)
    {
        var ctx = new ScoringContext(state, pending, cpuEntityId, intent);
        var list = new List<ScoredAction>(pending.LegalActions.Count);
        foreach (var action in pending.LegalActions)
        {
            float score = ScoreAction(ctx, action);
            var breakdown = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var c in _considerations)
            {
                if (!c.Handles(action.Kind)) continue;
                float w = _weights.Get(intent, c.Key);
                float r = Math.Clamp(c.Score(ctx, action), 0f, 1f);
                breakdown[c.Key] = r * w;
            }
            list.Add(new ScoredAction(action, score, breakdown));
        }
        list.Sort((a, b) => b.Score.CompareTo(a.Score));
        return list;
    }

    /// <summary>
    /// Public for inspection (editor preview-score, decision-frame
    /// rendering). Returns the raw weighted sum — the bot clamps its
    /// comparison, not the return value.
    /// </summary>
    public float ScoreAction(ScoringContext ctx, LegalAction action)
    {
        float total = 0f;
        foreach (var c in _considerations)
        {
            if (!c.Handles(action.Kind)) continue;
            float weight = _weights.Get(ctx.Intent, c.Key);
            if (weight == 0f) continue;
            float raw = c.Score(ctx, action);
            if (float.IsNaN(raw)) continue;
            float clamped = Math.Clamp(raw, 0f, 1f);
            total += clamped * weight;
        }
        return total;
    }

    /// <summary>
    /// Hash over (step count, prompt, action label). Stable across runs
    /// given the same game state + pending request + action list, so
    /// replays and tests produce identical tie-break results.
    /// </summary>
    private static int TieBreakHash(GameState state, InputRequest pending, LegalAction action)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + (int)(state.StepCount & 0xFFFFFFFF);
            h = h * 31 + StringHash(pending.Prompt);
            h = h * 31 + StringHash(action.Kind);
            h = h * 31 + StringHash(action.Label);
            return h;
        }
    }

    private static int StringHash(string s)
    {
        unchecked
        {
            int h = 0;
            foreach (char c in s) h = h * 31 + c;
            return h;
        }
    }

    /// <summary>
    /// Read-only peek for tests + the debug overlay: returns the per-action
    /// score breakdown sorted descending. Doesn't mutate anything.
    /// </summary>
    public IReadOnlyList<ScoredAction> ScoreAll(GameState state, InputRequest pending, int cpuEntityId)
    {
        var intent = _selectIntent(state, pending, cpuEntityId);
        return BuildRanked(state, pending, cpuEntityId, intent);
    }
}

/// <summary>One row of the score breakdown — action + total + per-consideration contribution.</summary>
public sealed record ScoredAction(
    LegalAction Action,
    float Score,
    IReadOnlyDictionary<string, float> Breakdown);
