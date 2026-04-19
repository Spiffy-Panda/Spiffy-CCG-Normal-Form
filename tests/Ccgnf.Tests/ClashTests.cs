using Ccgnf.Ast;
using Ccgnf.Interpreter;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using InterpreterRt = Ccgnf.Interpreter.Interpreter;

namespace Ccgnf.Tests;

/// <summary>
/// Tests for 8e — Unit play (Hand → Battlefield + arena pick) and the
/// Clash phase (attack/hold per Unit, Force damage to opponent's same-arena
/// Conduit). Fixture has two arenas, two conduits per player, and two
/// Warrior cards (force 2 and force 7).
/// </summary>
public class ClashTests
{
    private static AstFile LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "clash.ccgnf");
        Assert.True(File.Exists(path), $"Fixture missing: {path}");
        var loader = new ProjectLoader(NullLoggerFactory.Instance);
        var result = loader.LoadFromFiles(new[] { path });
        Assert.True(result.File is not null,
            "Fixture failed to load:\n" + string.Join("\n", result.Diagnostics));
        Assert.False(result.HasErrors,
            "Fixture has diagnostics:\n" + string.Join("\n", result.Diagnostics));
        return result.File!;
    }

    private static InterpreterRt NewInterpreter() =>
        new(NullLogger<InterpreterRt>.Instance, NullLoggerFactory.Instance);

    private static InterpreterOptions WithDeck(params string[] player1Deck) => new()
    {
        Seed = 1,
        InitialDecks = new IReadOnlyList<string>?[] { player1Deck, Array.Empty<string>() },
    };

    // -----------------------------------------------------------------------
    // Unit play — Hand → Battlefield with arena and stats attached.
    // -----------------------------------------------------------------------

    [Fact]
    public void UnitPlay_AsksForArena_ThenPlacesOnBattlefieldWithStats()
    {
        var file = LoadFixture();
        using var run = NewInterpreter().StartRun(file, WithDeck("Warrior"));

        // First pending: MainPhase. Play Warrior.
        var priority = run.WaitPending();
        Assert.NotNull(priority);
        var play = priority!.LegalActions.First(a => a.Kind == "play_card");
        Assert.Equal("Warrior", play.Metadata?["cardName"]);
        run.Submit(new RtSymbol(play.Label));

        // Second pending: Arena pick. Both Left and Right available.
        var arenaPrompt = run.WaitPending();
        Assert.NotNull(arenaPrompt);
        Assert.All(arenaPrompt!.LegalActions, a => Assert.Equal("target_arena", a.Kind));
        var leftPick = arenaPrompt.LegalActions.First(a => a.Metadata?["pos"] == "Left");
        int leftArenaId = int.Parse(leftPick.Metadata!["entityId"]);
        run.Submit(new RtSymbol(leftPick.Label));

        // Clash phase runs — Warrior is the only attacker. Force it to hold
        // for now; the damage test below exercises attack.
        var clashPrompt = run.WaitPending();
        Assert.NotNull(clashPrompt);
        run.Submit(new RtSymbol("hold"));

        Assert.Null(run.WaitPending());
        Assert.Equal(RunStatus.Completed, run.Status);

        // Warrior sits on Player1.Battlefield with force=2, ramparts=1,
        // owner=Player1, arena=Left, in_play=true.
        var p1 = run.State.NamedEntities["Player1"];
        var warriorId = p1.Zones["Battlefield"].Contents.Single();
        var warrior = run.State.Entities[warriorId];
        Assert.Equal("Warrior", warrior.DisplayName);
        Assert.Equal(2, warrior.Counters["force"]);
        Assert.Equal(1, warrior.Counters["max_ramparts"]);
        Assert.Equal(1, warrior.Counters["current_ramparts"]);
        Assert.Equal(p1.Id, warrior.OwnerId);
        Assert.Equal(new RtSymbol("Unit"), warrior.Characteristics["type"]);
        Assert.Equal(new RtBool(true), warrior.Characteristics["in_play"]);
        Assert.True(warrior.Parameters.TryGetValue("arena", out var arena));
        Assert.Equal(new RtSymbol("Left"), arena);
        Assert.True(warrior.Parameters.TryGetValue("arena_entity", out var arenaEnt));
        Assert.Equal(new RtEntityRef(leftArenaId), arenaEnt);

        // Hand empty, aether paid.
        Assert.Empty(p1.Zones["Hand"].Contents);
        Assert.Equal(9, p1.Counters["aether"]);  // 10 - 1
    }

    [Fact]
    public void UnitPlay_EmitsUnitEntered_AndCardPlayed()
    {
        var file = LoadFixture();
        var seen = new List<string>();
        var opts = WithDeck("Warrior");
        opts.OnEvent = (e, _) => seen.Add(e.TypeName);
        using var run = NewInterpreter().StartRun(file, opts);

        run.Submit(new RtSymbol(run.WaitPending()!.LegalActions.First(a => a.Kind == "play_card").Label));
        run.Submit(new RtSymbol(run.WaitPending()!.LegalActions.First(a => a.Metadata?["pos"] == "Left").Label));
        // WaitPending before each Submit — the hold we're submitting is for
        // the Clash prompt, which needs to have been published first.
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("hold"));
        Assert.Null(run.WaitPending());

        Assert.Contains("UnitEntered", seen);
        Assert.Contains("CardPlayed", seen);
    }

    // -----------------------------------------------------------------------
    // Clash damage.
    // -----------------------------------------------------------------------

    [Fact]
    public void Clash_Attack_DealsForceDamageToOpponentSameArenaConduit()
    {
        var file = LoadFixture();
        using var run = NewInterpreter().StartRun(file, WithDeck("Warrior"));

        // Play Warrior into Left.
        run.Submit(new RtSymbol(run.WaitPending()!.LegalActions.First(a => a.Kind == "play_card").Label));
        run.Submit(new RtSymbol(run.WaitPending()!.LegalActions.First(a => a.Metadata?["pos"] == "Left").Label));

        // Clash: attack.
        var clashPrompt = run.WaitPending();
        Assert.NotNull(clashPrompt);
        Assert.Equal("Clash.Left(Warrior)", clashPrompt!.Prompt);
        run.Submit(new RtSymbol("attack"));

        Assert.Null(run.WaitPending());

        int p2 = run.State.NamedEntities["Player2"].Id;
        var p2LeftConduit = run.State.Entities.Values.Single(e =>
            e.Kind == "Conduit" && e.OwnerId == p2 &&
            e.Parameters.TryGetValue("arena", out var a) && a is RtSymbol s && s.Name == "Left");
        Assert.Equal(5, p2LeftConduit.Counters["integrity"]);  // 7 - 2

        // P2's Right conduit untouched.
        var p2RightConduit = run.State.Entities.Values.Single(e =>
            e.Kind == "Conduit" && e.OwnerId == p2 &&
            e.Parameters.TryGetValue("arena", out var a) && a is RtSymbol s && s.Name == "Right");
        Assert.Equal(7, p2RightConduit.Counters["integrity"]);
    }

    [Fact]
    public void Clash_Hold_DealsNoDamage()
    {
        var file = LoadFixture();
        using var run = NewInterpreter().StartRun(file, WithDeck("Warrior"));

        run.Submit(new RtSymbol(run.WaitPending()!.LegalActions.First(a => a.Kind == "play_card").Label));
        run.Submit(new RtSymbol(run.WaitPending()!.LegalActions.First(a => a.Metadata?["pos"] == "Left").Label));
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("hold"));
        Assert.Null(run.WaitPending());

        foreach (var conduit in run.State.Entities.Values.Where(e => e.Kind == "Conduit"))
        {
            Assert.Equal(7, conduit.Counters["integrity"]);
        }

        foreach (var conduit in run.State.Entities.Values.Where(e => e.Kind == "Conduit"))
        {
            Assert.Equal(7, conduit.Counters["integrity"]);
        }
    }

    [Fact]
    public void Clash_NoUnits_EndsImmediatelyWithoutPrompting()
    {
        var file = LoadFixture();
        using var run = NewInterpreter().StartRun(file, WithDeck(/* empty */));

        // MainPhase prompt — just pass.
        var priority = run.WaitPending();
        Assert.NotNull(priority);
        run.Submit(new RtSymbol("pass"));

        // No arena prompts, no clash prompts (no units). Run completes.
        Assert.Null(run.WaitPending());
        Assert.Equal(RunStatus.Completed, run.Status);
    }

    [Fact]
    public void Clash_Attack_WithEnoughForce_CollapsesConduit_ViaSba()
    {
        var file = LoadFixture();
        var seen = new List<string>();
        var opts = WithDeck("BigWarrior");
        opts.OnEvent = (e, _) => seen.Add(e.TypeName);
        using var run = NewInterpreter().StartRun(file, opts);

        run.Submit(new RtSymbol(run.WaitPending()!.LegalActions.First(a => a.Kind == "play_card").Label));
        run.Submit(new RtSymbol(run.WaitPending()!.LegalActions.First(a => a.Metadata?["pos"] == "Left").Label));
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("attack"));
        Assert.Null(run.WaitPending());

        // 8f's SBA fires: integrity hit 0 → ConduitCollapsed event.
        Assert.Contains("ConduitCollapsed", seen);

        int p2 = run.State.NamedEntities["Player2"].Id;
        var p2LeftConduit = run.State.Entities.Values.Single(e =>
            e.Kind == "Conduit" && e.OwnerId == p2 &&
            e.Parameters.TryGetValue("arena", out var a) && a is RtSymbol s && s.Name == "Left");
        Assert.Equal(0, p2LeftConduit.Counters["integrity"]);
        Assert.Contains("collapsed", p2LeftConduit.Tags);
    }
}
