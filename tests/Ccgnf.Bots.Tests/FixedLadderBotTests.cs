namespace Ccgnf.Bots.Tests;

/// <summary>
/// Exercises the ladder rules that used to live inline in
/// <c>Room.ChooseCpuAction</c>. These tests are the regression floor for
/// the 10.2a extraction — if any break, the lift changed behaviour.
/// </summary>
public class FixedLadderBotTests
{
    private static InputRequest Req(params LegalAction[] actions) =>
        new("prompt", 1, actions);

    private static (GameState state, int cpuId, int oppId) MinimalTwoPlayer()
    {
        var state = new GameState();
        var p1 = state.AllocateEntity("Player", "CPU");
        var p2 = state.AllocateEntity("Player", "Human");
        state.Players.Add(p1);
        state.Players.Add(p2);
        return (state, p1.Id, p2.Id);
    }

    [Fact]
    public void EmptyLegalActionsReturnsPass()
    {
        var bot = new FixedLadderBot();
        var (state, cpu, _) = MinimalTwoPlayer();
        var result = bot.Choose(state, Req(), cpu);
        Assert.Equal("pass", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void ClashAttackAlwaysTaken()
    {
        var bot = new FixedLadderBot();
        var (state, cpu, _) = MinimalTwoPlayer();
        var req = Req(
            new LegalAction("declare_attacker", "attack"),
            new LegalAction("declare_attacker", "pass"));
        var result = bot.Choose(state, req, cpu);
        Assert.Equal("attack", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void TargetPicksOpponentWithLowestHp()
    {
        var bot = new FixedLadderBot();
        var (state, cpu, opp) = MinimalTwoPlayer();

        var high = state.AllocateEntity("Conduit", "OppConduit1");
        high.OwnerId = opp;
        high.Counters["integrity"] = 5;

        var low = state.AllocateEntity("Conduit", "OppConduit2");
        low.OwnerId = opp;
        low.Counters["integrity"] = 2;

        var req = Req(
            new LegalAction("target_entity", $"target:{high.Id}",
                new Dictionary<string, string> { ["entityId"] = high.Id.ToString() }),
            new LegalAction("target_entity", $"target:{low.Id}",
                new Dictionary<string, string> { ["entityId"] = low.Id.ToString() }));

        var result = bot.Choose(state, req, cpu);
        Assert.Equal($"target:{low.Id}", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void TargetNeverPicksFriendly()
    {
        var bot = new FixedLadderBot();
        var (state, cpu, opp) = MinimalTwoPlayer();

        var friendly = state.AllocateEntity("Conduit", "CpuConduit");
        friendly.OwnerId = cpu;
        friendly.Counters["integrity"] = 1; // tempting but must skip

        var enemy = state.AllocateEntity("Conduit", "OppConduit");
        enemy.OwnerId = opp;
        enemy.Counters["integrity"] = 5;

        var req = Req(
            new LegalAction("target_entity", $"target:{friendly.Id}",
                new Dictionary<string, string> { ["entityId"] = friendly.Id.ToString() }),
            new LegalAction("target_entity", $"target:{enemy.Id}",
                new Dictionary<string, string> { ["entityId"] = enemy.Id.ToString() }));

        var result = bot.Choose(state, req, cpu);
        Assert.Equal($"target:{enemy.Id}", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void PlayCardPrefersUnitsOverManeuvers()
    {
        var bot = new FixedLadderBot();
        var (state, cpu, _) = MinimalTwoPlayer();
        var req = Req(
            new LegalAction("play_card", "play:maneuver", new Dictionary<string, string>
            {
                ["cost"] = "1", ["type"] = "Maneuver",
            }),
            new LegalAction("play_card", "play:unit", new Dictionary<string, string>
            {
                ["cost"] = "2", ["type"] = "Unit",
            }));

        var result = bot.Choose(state, req, cpu);
        Assert.Equal("play:unit", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void PlayCardTieBreaksByLowestCost()
    {
        var bot = new FixedLadderBot();
        var (state, cpu, _) = MinimalTwoPlayer();
        var req = Req(
            new LegalAction("play_card", "play:big", new Dictionary<string, string>
            {
                ["cost"] = "3", ["type"] = "Unit",
            }),
            new LegalAction("play_card", "play:cheap", new Dictionary<string, string>
            {
                ["cost"] = "1", ["type"] = "Unit",
            }));

        var result = bot.Choose(state, req, cpu);
        Assert.Equal("play:cheap", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void PlayCardFallsBackToManeuverWhenNoUnitsInHand()
    {
        var bot = new FixedLadderBot();
        var (state, cpu, _) = MinimalTwoPlayer();
        var req = Req(
            new LegalAction("play_card", "play:m1", new Dictionary<string, string>
            {
                ["cost"] = "2", ["type"] = "Maneuver",
            }),
            new LegalAction("play_card", "play:m2", new Dictionary<string, string>
            {
                ["cost"] = "1", ["type"] = "Maneuver",
            }));

        var result = bot.Choose(state, req, cpu);
        Assert.Equal("play:m2", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void ArenaPrefersOverlapWithOpponentUnit()
    {
        var bot = new FixedLadderBot();
        var (state, cpu, opp) = MinimalTwoPlayer();

        // Opponent has a Unit in arena "right" → bot should pick right.
        var oppUnit = state.AllocateEntity("Card", "OppUnit");
        oppUnit.OwnerId = opp;
        oppUnit.Characteristics["in_play"] = new RtBool(true);
        oppUnit.Parameters["arena"] = new RtSymbol("right");

        var req = Req(
            new LegalAction("target_arena", "arena:left",
                new Dictionary<string, string> { ["pos"] = "left" }),
            new LegalAction("target_arena", "arena:right",
                new Dictionary<string, string> { ["pos"] = "right" }));

        var result = bot.Choose(state, req, cpu);
        Assert.Equal("arena:right", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void ArenaFallsBackToStandingConduitWhenNoOverlap()
    {
        var bot = new FixedLadderBot();
        var (state, cpu, opp) = MinimalTwoPlayer();

        // Opponent conduit collapsed on "left", standing on "right" — pick right.
        var collapsed = state.AllocateEntity("Conduit", "oppLeft");
        collapsed.OwnerId = opp;
        collapsed.Tags.Add("collapsed");
        collapsed.Parameters["arena"] = new RtSymbol("left");

        var standing = state.AllocateEntity("Conduit", "oppRight");
        standing.OwnerId = opp;
        standing.Parameters["arena"] = new RtSymbol("right");

        var req = Req(
            new LegalAction("target_arena", "arena:left",
                new Dictionary<string, string> { ["pos"] = "left" }),
            new LegalAction("target_arena", "arena:right",
                new Dictionary<string, string> { ["pos"] = "right" }));

        var result = bot.Choose(state, req, cpu);
        Assert.Equal("arena:right", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void ChoicePrefersPass()
    {
        var bot = new FixedLadderBot();
        var (state, cpu, _) = MinimalTwoPlayer();
        var req = Req(
            new LegalAction("choice_option", "mulligan"),
            new LegalAction("choice_option", "pass"));

        var result = bot.Choose(state, req, cpu);
        Assert.Equal("pass", Assert.IsType<RtSymbol>(result).Name);
    }

    [Fact]
    public void UnknownActionsFallThroughToFirst()
    {
        var bot = new FixedLadderBot();
        var (state, cpu, _) = MinimalTwoPlayer();
        var req = Req(
            new LegalAction("some_unknown_kind", "first"),
            new LegalAction("some_unknown_kind", "second"));

        var result = bot.Choose(state, req, cpu);
        Assert.Equal("first", Assert.IsType<RtSymbol>(result).Name);
    }
}
