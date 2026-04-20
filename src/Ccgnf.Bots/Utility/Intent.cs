namespace Ccgnf.Bots.Utility;

/// <summary>
/// Top-level behavioural intents selected by the phase-BT before each
/// utility decision. Intents shift the weights the scorer applies: under
/// <see cref="DefendConduit"/>, removal considerations get boosted;
/// under <see cref="Pushing"/>, Resonance-alignment considerations
/// dominate.
/// </summary>
public enum Intent
{
    /// <summary>Fallback used when no other gate in the phase-BT fires.</summary>
    Default,
    /// <summary>Rounds 1–3 of the game; aggressive on-curve plays.</summary>
    EarlyTempo,
    /// <summary>Banner in hand — bias toward plays that accumulate Resonance.</summary>
    Pushing,
    /// <summary>At least one friendly conduit at low integrity.</summary>
    DefendConduit,
    /// <summary>Opponent at one standing conduit — close the game.</summary>
    LethalCheck,
}
