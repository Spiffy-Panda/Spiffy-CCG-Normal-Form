using Ccgnf.Ast;
using Ccgnf.Interpreter;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using InterpreterRt = Ccgnf.Interpreter.Interpreter;

namespace Ccgnf.Tests;

/// <summary>
/// Tests for the Main-phase card-play protocol added in step 8c. Uses a
/// self-contained fixture (<c>fixtures/card-play-noop.ccgnf</c>) whose
/// <c>GameStart</c> trigger opens a priority window for Player1 — the
/// handle surfaces the legal plays, a submit resolves the card with cost
/// paid and a flag flipped.
/// </summary>
public class CardPlayTests
{
    private static AstFile LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "card-play-noop.ccgnf");
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

    private static InterpreterOptions WithDeck(params string[] player1Deck)
    {
        // Positional InitialDecks: [Player1 deck, Player2 deck]. Player2 gets
        // an empty deck so its Main phase (never opens in this fixture) has
        // nothing to offer.
        return new InterpreterOptions
        {
            Seed = 1,
            InitialDecks = new IReadOnlyList<string>?[]
            {
                player1Deck,
                Array.Empty<string>(),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Legal actions surfaced
    // -----------------------------------------------------------------------

    [Fact]
    public void MainPhase_WithAffordableCard_OffersPassAndPlay()
    {
        var file = LoadFixture();
        using var run = NewInterpreter().StartRun(file, WithDeck("NoOpBlast"));

        var pending = run.WaitPending();
        Assert.NotNull(pending);
        Assert.Equal(RunStatus.WaitingForInput, run.Status);
        Assert.Contains(pending!.LegalActions, a => a.Label == "pass");
        var play = pending.LegalActions.FirstOrDefault(a => a.Kind == "play_card");
        Assert.NotNull(play);
        Assert.StartsWith("play:", play!.Label);
        Assert.Equal("NoOpBlast", play.Metadata?["cardName"]);
        Assert.Equal("1", play.Metadata?["cost"]);

        run.Submit(new RtSymbol("pass"));
        Assert.Null(run.WaitPending());
        Assert.Equal(RunStatus.Completed, run.Status);
    }

    [Fact]
    public void MainPhase_UnaffordableCard_NotInLegalActions()
    {
        var file = LoadFixture();
        using var run = NewInterpreter().StartRun(file, WithDeck("CostlyBlast"));

        var pending = run.WaitPending();
        Assert.NotNull(pending);
        // Only pass — CostlyBlast costs 99, player has 3 aether.
        Assert.Single(pending!.LegalActions);
        Assert.Equal("pass", pending.LegalActions[0].Label);

        run.Submit(new RtSymbol("pass"));
        Assert.Null(run.WaitPending());
    }

    // -----------------------------------------------------------------------
    // Playing the card — cost, effect, zone move.
    // -----------------------------------------------------------------------

    [Fact]
    public void PlayCard_PaysCostAndResolvesEffectAndMovesToCache()
    {
        var file = LoadFixture();
        using var run = NewInterpreter().StartRun(file, WithDeck("NoOpBlast"));

        var pending = run.WaitPending();
        Assert.NotNull(pending);
        var play = pending!.LegalActions.First(a => a.Kind == "play_card");
        run.Submit(new RtSymbol(play.Label));

        Assert.Null(run.WaitPending());
        Assert.Equal(RunStatus.Completed, run.Status);

        var state = run.State;
        var player1 = state.NamedEntities["Player1"];

        // Aether paid: 3 starting - 1 cost = 2.
        Assert.Equal(2, player1.Counters["aether"]);

        // OnResolve fired — flag flipped on controller.
        Assert.True(player1.Characteristics.TryGetValue("noop_blast_fired", out var flag));
        Assert.IsType<RtBool>(flag);
        Assert.True(((RtBool)flag).V);

        // Card moved Hand → Cache.
        Assert.Empty(player1.Zones["Hand"].Contents);
        Assert.Single(player1.Zones["Cache"].Contents);
        int cardId = player1.Zones["Cache"].Contents[0];
        Assert.Equal("NoOpBlast", state.Entities[cardId].DisplayName);
    }

    [Fact]
    public void PlayCard_EmitsCardPlayedEvent()
    {
        var file = LoadFixture();
        var events = new List<string>();
        var opts = WithDeck("NoOpBlast");
        opts.OnEvent = (ev, _) => events.Add(ev.TypeName);
        using var run = NewInterpreter().StartRun(file, opts);

        var pending = run.WaitPending();
        var play = pending!.LegalActions.First(a => a.Kind == "play_card");
        run.Submit(new RtSymbol(play.Label));
        Assert.Null(run.WaitPending());

        Assert.Contains("CardPlayed", events);
    }

    [Fact]
    public void PassPriority_LeavesStateUntouched()
    {
        var file = LoadFixture();
        using var run = NewInterpreter().StartRun(file, WithDeck("NoOpBlast"));

        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));
        Assert.Null(run.WaitPending());
        Assert.Equal(RunStatus.Completed, run.Status);

        var player1 = run.State.NamedEntities["Player1"];
        Assert.Equal(3, player1.Counters["aether"]);
        Assert.False(player1.Characteristics.ContainsKey("noop_blast_fired"));
        Assert.Single(player1.Zones["Hand"].Contents);
        Assert.Empty(player1.Zones["Cache"].Contents);
    }
}
