using Ccgnf.Interpreter;

namespace Ccgnf.Bots.Utility;

/// <summary>
/// Immutable snapshot passed to every <see cref="IConsideration"/> when
/// scoring a <see cref="LegalAction"/>. Built once per
/// <see cref="UtilityBot.Choose"/> call; considerations read from it and
/// never mutate.
/// </summary>
public sealed class ScoringContext
{
    public GameState State { get; }
    public InputRequest Pending { get; }
    public int CpuEntityId { get; }
    public Intent Intent { get; }
    public PlayerView Cpu { get; }
    public PlayerView Opponent { get; }

    public ScoringContext(
        GameState state,
        InputRequest pending,
        int cpuEntityId,
        Intent intent)
    {
        State = state;
        Pending = pending;
        CpuEntityId = cpuEntityId;
        Intent = intent;
        Cpu = PlayerView.Build(state, cpuEntityId);
        int opponentId = state.Players.FirstOrDefault(p => p.Id != cpuEntityId)?.Id ?? 0;
        Opponent = opponentId == 0 ? PlayerView.Empty : PlayerView.Build(state, opponentId);
    }
}

/// <summary>
/// Cached lookups for one player. Pre-computed so considerations don't
/// walk the entity dictionary repeatedly.
/// </summary>
public sealed record PlayerView
{
    public int EntityId { get; init; }
    /// <summary>Current aether counter on the Player entity. 0 when absent.</summary>
    public int Aether { get; init; }
    /// <summary>Cards in the player's hand zone.</summary>
    public int HandCount { get; init; }
    /// <summary>Hand cap — from the Player's <c>hand_cap</c> characteristic. 7 as a safe default.</summary>
    public int HandCap { get; init; }
    /// <summary>Minimum integrity across the player's standing conduits, or <c>int.MaxValue</c> if none.</summary>
    public int MinConduitIntegrity { get; init; }
    /// <summary>Count of standing (not <c>collapsed</c>) conduits.</summary>
    public int StandingConduits { get; init; }

    /// <summary>
    /// Opponent's standing conduit entity per arena symbol ("left", "middle",
    /// "right"). Absent when that arena's conduit has collapsed.
    /// </summary>
    public IReadOnlyDictionary<string, Entity> ConduitByArena { get; init; } =
        new Dictionary<string, Entity>();

    /// <summary>
    /// Count of in-play Unit cards this player controls in each arena.
    /// Arenas without a unit are simply absent from the dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, int> UnitsByArena { get; init; } =
        new Dictionary<string, int>();

    /// <summary>
    /// Aggregate force of this player's in-play Units in each arena.
    /// Used for one-ply threat checks in
    /// <see cref="Considerations.ThreatAvoidanceConsideration"/>.
    /// </summary>
    public IReadOnlyDictionary<string, int> UnitForceByArena { get; init; } =
        new Dictionary<string, int>();

    public static PlayerView Empty { get; } = new()
    {
        EntityId = 0,
        Aether = 0,
        HandCount = 0,
        HandCap = 7,
        MinConduitIntegrity = int.MaxValue,
        StandingConduits = 0,
    };

    public static PlayerView Build(GameState state, int playerId)
    {
        if (!state.Entities.TryGetValue(playerId, out var player))
            return Empty with { EntityId = playerId };

        int aether = player.Counters.TryGetValue("aether", out var a) ? a : 0;
        int handCount = player.Zones.TryGetValue("hand", out var hand) ? hand.Count : 0;
        int handCap = 7;
        if (player.Characteristics.TryGetValue("hand_cap", out var hc) && hc is RtInt hcInt)
            handCap = hcInt.V;

        int minIntegrity = int.MaxValue;
        int standing = 0;
        var conduits = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var units = new Dictionary<string, int>(StringComparer.Ordinal);
        var force = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entity in state.Entities.Values)
        {
            if (entity.OwnerId != playerId) continue;

            if (entity.Kind == "Conduit")
            {
                if (entity.Tags.Contains("collapsed")) continue;
                standing++;
                if (entity.Counters.TryGetValue("integrity", out var integrity) && integrity < minIntegrity)
                    minIntegrity = integrity;
                if (entity.Parameters.TryGetValue("arena", out var arenaParam) && arenaParam is RtSymbol asym)
                    conduits[asym.Name] = entity;
                continue;
            }

            if (entity.Kind == "Card")
            {
                bool inPlay = entity.Characteristics.TryGetValue("in_play", out var ip)
                    && ip is RtBool rb && rb.V;
                if (!inPlay) continue;
                if (!entity.Parameters.TryGetValue("arena", out var arenaParam)) continue;
                if (arenaParam is not RtSymbol asym) continue;
                units[asym.Name] = units.GetValueOrDefault(asym.Name) + 1;
                int f = entity.Counters.TryGetValue("force", out var fv) ? fv : 0;
                force[asym.Name] = force.GetValueOrDefault(asym.Name) + f;
            }
        }

        return new PlayerView
        {
            EntityId = playerId,
            Aether = aether,
            HandCount = handCount,
            HandCap = handCap,
            MinConduitIntegrity = minIntegrity,
            StandingConduits = standing,
            ConduitByArena = conduits,
            UnitsByArena = units,
            UnitForceByArena = force,
        };
    }
}
