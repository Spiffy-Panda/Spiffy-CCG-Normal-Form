using Ccgnf.Bots.Utility;
using Ccgnf.Interpreter;

namespace Ccgnf.Bots.Bt;

/// <summary>
/// Variable + action bridge the BT runner talks to. For the phase
/// selector, the "action" is to set <see cref="PhaseBtContext.ChosenIntent"/>;
/// the "variables" are named integers and booleans the engine publishes
/// about the current state.
/// </summary>
public interface IBtContext
{
    /// <summary>
    /// Resolve a named variable to a float. Unknown names return 0 —
    /// matches the reference implementation's contract and lets gates
    /// with typos fail silently rather than crashing.
    /// </summary>
    float ResolveVariable(string name);

    /// <summary>
    /// Execute an action leaf. Returns <see cref="BtStatus.Success"/> if
    /// the action was recognised, <see cref="BtStatus.Failure"/>
    /// otherwise. The phase selector's actions are all
    /// <c>intent:&lt;name&gt;</c> strings that record the pick.
    /// </summary>
    BtStatus ExecuteAction(string action);
}

/// <summary>
/// Concrete <see cref="IBtContext"/> built from the Resonance
/// <see cref="ScoringContext"/>. Exposes the variables the default
/// phase-BT references: round number, banner-matched cards in hand,
/// min own conduit integrity, opponent's standing-conduit count.
/// Action leaves of the form <c>intent:&lt;name&gt;</c> set
/// <see cref="ChosenIntent"/>.
/// </summary>
public sealed class PhaseBtContext : IBtContext
{
    private readonly GameState _state;
    private readonly int _cpuEntityId;
    private readonly int _opponentEntityId;

    public Intent ChosenIntent { get; private set; } = Intent.Default;

    public PhaseBtContext(GameState state, int cpuEntityId)
    {
        _state = state;
        _cpuEntityId = cpuEntityId;
        _opponentEntityId = state.Players.FirstOrDefault(p => p.Id != cpuEntityId)?.Id ?? 0;
    }

    public float ResolveVariable(string name) => name switch
    {
        "turn_number" => ReadCounter(_state.Game, "turn_number", fallback: 1),
        "round_number" => ReadCounter(_state.Game, "round_number", fallback: 1),
        "min_own_conduit_integrity" => ComputeMinConduit(_cpuEntityId),
        "min_opp_conduit_integrity" => ComputeMinConduit(_opponentEntityId),
        "opponent_standing_conduits" => CountStandingConduits(_opponentEntityId),
        "own_standing_conduits" => CountStandingConduits(_cpuEntityId),
        "banner_matches_in_hand" => CountBannerMatches(),
        _ => 0f,
    };

    public BtStatus ExecuteAction(string action)
    {
        if (action is null) return BtStatus.Failure;
        if (!action.StartsWith("intent:", StringComparison.Ordinal)) return BtStatus.Failure;
        var name = action.Substring("intent:".Length);
        // Case-insensitive match against the Intent enum, ignoring underscores
        // so phase-bt.json can use snake_case ("early_tempo" → EarlyTempo).
        var normalised = name.Replace("_", "", StringComparison.Ordinal);
        if (!Enum.TryParse<Intent>(normalised, ignoreCase: true, out var intent))
            return BtStatus.Failure;
        ChosenIntent = intent;
        return BtStatus.Success;
    }

    private float ReadCounter(Entity? entity, string key, int fallback)
    {
        if (entity is null) return fallback;
        return entity.Counters.TryGetValue(key, out var v) ? v : fallback;
    }

    private float ComputeMinConduit(int playerId)
    {
        if (playerId == 0) return int.MaxValue;
        int min = int.MaxValue;
        foreach (var e in _state.Entities.Values)
        {
            if (e.Kind != "Conduit") continue;
            if (e.OwnerId != playerId) continue;
            if (e.Tags.Contains("collapsed")) continue;
            if (!e.Counters.TryGetValue("integrity", out var i)) continue;
            if (i > 0 && i < min) min = i;
        }
        return min == int.MaxValue ? 99 : min;
    }

    private float CountStandingConduits(int playerId)
    {
        if (playerId == 0) return 0;
        int n = 0;
        foreach (var e in _state.Entities.Values)
        {
            if (e.Kind != "Conduit") continue;
            if (e.OwnerId != playerId) continue;
            if (e.Tags.Contains("collapsed")) continue;
            n++;
        }
        return n;
    }

    private float CountBannerMatches()
    {
        if (_cpuEntityId == 0) return 0;
        if (!_state.Entities.TryGetValue(_cpuEntityId, out var cpu)) return 0;

        string? banner = null;
        if (cpu.Characteristics.TryGetValue("banner", out var b) && b is RtSymbol bs)
            banner = bs.Name;
        if (banner is null) return 0;

        if (!cpu.Zones.TryGetValue("hand", out var hand)) return 0;
        int n = 0;
        foreach (var cardId in hand.Contents)
        {
            if (!_state.Entities.TryGetValue(cardId, out var card)) continue;
            if (!card.Characteristics.TryGetValue("faction", out var faction)) continue;
            if (faction is RtSymbol fs && fs.Name == banner) n++;
        }
        return n;
    }
}
