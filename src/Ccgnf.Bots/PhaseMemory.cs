using Ccgnf.Bots.Utility;

namespace Ccgnf.Bots;

/// <summary>
/// Across-decision memory carried by the hosting <c>Room</c>. The bot
/// itself stays stateless with respect to history — anything that needs
/// to persist between <c>Choose</c> calls lives here.
/// <para>
/// Primary consumer is 10.2g's sticky-intent logic: a recently-chosen
/// <see cref="Intent"/> gets a bias multiplier on the BT selector so the
/// bot doesn't flip-flop mid-phase.
/// </para>
/// </summary>
public sealed class PhaseMemory
{
    /// <summary>The intent picked on the previous decision, or <see cref="Intent.Default"/> when fresh.</summary>
    public Intent LastIntent { get; set; } = Intent.Default;

    /// <summary>
    /// Decisions elapsed since <see cref="LastIntent"/> was set. 0 when just
    /// set, bumped on each subsequent decision. Initialised to a large
    /// value so a fresh memory reports no bias — stickiness only kicks in
    /// after the first real recording.
    /// </summary>
    public int DecisionsSinceIntentSet { get; set; } = int.MaxValue;

    /// <summary>Last phase name seen (Rise/Channel/Clash/Fall/Pass). Empty when fresh.</summary>
    public string LastPhase { get; set; } = "";

    /// <summary>Half-life of the stickiness bias, in decisions.</summary>
    public int StickyMaxAge { get; set; } = 5;

    /// <summary>Multiplicative bias applied to the matching intent gate while stickiness is active.</summary>
    public float StickyBias { get; set; } = 1.5f;

    /// <summary>Per-decision linear decay toward 1.0 (no bias).</summary>
    public float StickyBiasDecay { get; set; } = 0.1f;

    /// <summary>
    /// Current effective bias: <see cref="StickyBias"/> fading to 1.0 over
    /// <see cref="StickyMaxAge"/> decisions. Returns 1.0 once sticky expires.
    /// </summary>
    public float CurrentBias
    {
        get
        {
            if (DecisionsSinceIntentSet >= StickyMaxAge) return 1.0f;
            float raw = StickyBias - DecisionsSinceIntentSet * StickyBiasDecay;
            return raw <= 1.0f ? 1.0f : raw;
        }
    }

    /// <summary>
    /// Called after a decision resolves. Bumps the age counter and — if
    /// the game phase changed — resets stickiness so a new phase gets a
    /// fresh BT evaluation.
    /// </summary>
    public void OnDecisionRecorded(Intent chosenIntent, string currentPhase)
    {
        bool phaseChanged = !string.IsNullOrEmpty(LastPhase) && currentPhase != LastPhase;
        if (phaseChanged)
        {
            LastIntent = chosenIntent;
            DecisionsSinceIntentSet = 0;
            LastPhase = currentPhase;
            return;
        }

        if (chosenIntent != LastIntent)
        {
            LastIntent = chosenIntent;
            DecisionsSinceIntentSet = 0;
        }
        else
        {
            DecisionsSinceIntentSet++;
        }
        LastPhase = currentPhase;
    }
}
