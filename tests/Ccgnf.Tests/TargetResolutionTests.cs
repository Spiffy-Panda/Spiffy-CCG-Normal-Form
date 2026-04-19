using Ccgnf.Ast;
using Ccgnf.Interpreter;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using InterpreterRt = Ccgnf.Interpreter.Interpreter;

namespace Ccgnf.Tests;

/// <summary>
/// Tests for Target(…) resolution added in step 8d. The fixture declares
/// two Conduits + one Maneuver that deals 2 damage to a Conduit on resolve.
/// Playing the card surfaces a target-entity pending with both Conduits as
/// legal; submitting one reduces its integrity.
/// </summary>
public class TargetResolutionTests
{
    private static AstFile LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "card-play-target.ccgnf");
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

    [Fact]
    public void Target_PublishesBothConduitsAsLegal()
    {
        var file = LoadFixture();
        using var run = NewInterpreter().StartRun(file, WithDeck("TargetedBlast"));

        // First pending: priority window — pick play:<id>.
        var priority = run.WaitPending();
        Assert.NotNull(priority);
        var play = priority!.LegalActions.First(a => a.Kind == "play_card");
        run.Submit(new RtSymbol(play.Label));

        // Second pending: target — two Conduits.
        var target = run.WaitPending();
        Assert.NotNull(target);
        Assert.Equal("Target(target)", target!.Prompt);
        Assert.Equal(2, target.LegalActions.Count);
        Assert.All(target.LegalActions, a =>
        {
            Assert.Equal("target_entity", a.Kind);
            Assert.StartsWith("target:", a.Label);
            Assert.Equal("Conduit", a.Metadata?["kind"]);
        });

        // Bail out cleanly.
        run.Stop();
        run.WaitForExit(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Target_DealsDamageToChosenConduit()
    {
        var file = LoadFixture();
        using var run = NewInterpreter().StartRun(file, WithDeck("TargetedBlast"));

        var priority = run.WaitPending();
        var play = priority!.LegalActions.First(a => a.Kind == "play_card");
        run.Submit(new RtSymbol(play.Label));

        var target = run.WaitPending();
        // Pick ConduitRight specifically.
        int rightConduitId = run.State.NamedEntities["ConduitRight"].Id;
        var pick = target!.LegalActions.First(a => a.Metadata?["entityId"] == rightConduitId.ToString());
        run.Submit(new RtSymbol(pick.Label));

        Assert.Null(run.WaitPending());
        Assert.Equal(RunStatus.Completed, run.Status);

        var p2Conduit = run.State.Entities[rightConduitId];
        Assert.Equal(5, p2Conduit.Counters["integrity"]);  // 7 - 2

        // Untouched conduit stays at 7.
        int leftConduitId = run.State.NamedEntities["ConduitLeft"].Id;
        Assert.Equal(7, run.State.Entities[leftConduitId].Counters["integrity"]);
    }

    [Fact]
    public void Target_UnrecognisedChoice_NoOps()
    {
        var file = LoadFixture();
        using var run = NewInterpreter().StartRun(file, WithDeck("TargetedBlast"));

        var priority = run.WaitPending();
        var play = priority!.LegalActions.First(a => a.Kind == "play_card");
        run.Submit(new RtSymbol(play.Label));

        var target = run.WaitPending();
        Assert.NotNull(target);
        // Submit garbage — the effect evaluator bails; no damage dealt.
        run.Submit(new RtSymbol("not-a-target"));

        Assert.Null(run.WaitPending());
        Assert.Equal(RunStatus.Completed, run.Status);

        int rightConduitId = run.State.NamedEntities["ConduitRight"].Id;
        Assert.Equal(7, run.State.Entities[rightConduitId].Counters["integrity"]);
    }
}
