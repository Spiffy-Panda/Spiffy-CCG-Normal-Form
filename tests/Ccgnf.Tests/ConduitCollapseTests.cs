using Ccgnf.Ast;
using Ccgnf.Interpreter;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using InterpreterRt = Ccgnf.Interpreter.Interpreter;

namespace Ccgnf.Tests;

/// <summary>
/// Tests for the v1 SBA pass added in 8f (conduit collapse) and 8g
/// (two-conduits-lost victory). The fixture seats four Conduits — two per
/// player — and a single DoubleAnnihilate that picks two targets and
/// deals 7 damage to each. One play collapses both of P2's conduits; the
/// SBA pass fires ConduitCollapsed × 2 followed by GameEnd(loser=P2).
/// </summary>
public class ConduitCollapseTests
{
    private static AstFile LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "conduit-collapse.ccgnf");
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

    private static (InterpreterRun run, List<GameEvent> events) StartWithHand(AstFile file)
    {
        var events = new List<GameEvent>();
        var run = NewInterpreter().StartRun(file, new InterpreterOptions
        {
            Seed = 1,
            InitialDecks = new IReadOnlyList<string>?[]
            {
                new[] { "DoubleAnnihilate" },
                Array.Empty<string>(),
            },
            OnEvent = (ev, _) => events.Add(ev),
        });
        return (run, events);
    }

    [Fact]
    public void Conduits_Owned_ByOwnerField_GetOwnerIdSet()
    {
        // Sanity: InstantiateEntity passes owner=PlayerN; 8f's StateBuilder
        // change hoists that into Entity.OwnerId. If this ever regresses,
        // the victory check in RunSbaPass can't attribute collapses.
        var file = LoadFixture();
        using var run = NewInterpreter().StartRun(file, new InterpreterOptions
        {
            Seed = 1,
            InitialDecks = new IReadOnlyList<string>?[] { Array.Empty<string>(), Array.Empty<string>() },
        });

        // Wait for the MainPhase pending to drain setup; then bail — we
        // only want to inspect post-Setup state.
        var pending = run.WaitPending();
        Assert.NotNull(pending);

        var conduits = run.State.Entities.Values.Where(e => e.Kind == "Conduit").ToList();
        Assert.Equal(4, conduits.Count);

        int p1 = run.State.NamedEntities["Player1"].Id;
        int p2 = run.State.NamedEntities["Player2"].Id;
        Assert.Equal(2, conduits.Count(c => c.OwnerId == p1));
        Assert.Equal(2, conduits.Count(c => c.OwnerId == p2));

        run.Stop();
        run.WaitForExit(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void CollapseSba_Fires_ConduitCollapsed_Event()
    {
        var file = LoadFixture();
        var (run, events) = StartWithHand(file);
        using var _ = run;

        var priority = run.WaitPending();
        var play = priority!.LegalActions.First(a => a.Kind == "play_card");
        run.Submit(new RtSymbol(play.Label));

        // Pick any P2 Conduit for the first target.
        int p2 = run.State.NamedEntities["Player2"].Id;
        var p2Conduits = run.State.Entities.Values
            .Where(e => e.Kind == "Conduit" && e.OwnerId == p2)
            .Select(e => e.Id)
            .ToList();
        Assert.Equal(2, p2Conduits.Count);

        var t1 = run.WaitPending();
        var p1Pick = t1!.LegalActions.First(a => a.Metadata?["entityId"] == p2Conduits[0].ToString());
        run.Submit(new RtSymbol(p1Pick.Label));

        // Second target pick — the still-standing P2 conduit (or any other,
        // but pick P1's to verify we can *not* pick a collapsed one yet and
        // victory only triggers for 2 of the same owner).
        var t2 = run.WaitPending();
        // Same-owner conduits should still appear, including the already-
        // damaged one (SBAs run between events, not inline with Target
        // evaluation). Pick the other P2 conduit to trigger victory.
        var p2Pick = t2!.LegalActions.First(a => a.Metadata?["entityId"] == p2Conduits[1].ToString());
        run.Submit(new RtSymbol(p2Pick.Label));

        // Run drains: each Target's effect enqueues DamageDealt, then SBA
        // sees integrity=0 on two P2 conduits and enqueues ConduitCollapsed
        // + GameEnd. Game ends.
        Assert.Null(run.WaitPending());
        Assert.Equal(RunStatus.Completed, run.Status);

        var collapseEvents = events.Where(e => e.TypeName == "ConduitCollapsed").ToList();
        Assert.Equal(2, collapseEvents.Count);
        foreach (var ce in collapseEvents)
        {
            Assert.True(ce.Fields.TryGetValue("owner", out var owner));
            Assert.Equal(new RtEntityRef(p2), owner);
        }
    }

    [Fact]
    public void VictorySba_Fires_GameEnd_WithLoserWinnerReason()
    {
        var file = LoadFixture();
        var (run, events) = StartWithHand(file);
        using var _ = run;

        var priority = run.WaitPending();
        run.Submit(new RtSymbol(priority!.LegalActions.First(a => a.Kind == "play_card").Label));

        int p1 = run.State.NamedEntities["Player1"].Id;
        int p2 = run.State.NamedEntities["Player2"].Id;
        var p2Conduits = run.State.Entities.Values
            .Where(e => e.Kind == "Conduit" && e.OwnerId == p2)
            .Select(e => e.Id)
            .ToList();

        var t1 = run.WaitPending();
        run.Submit(new RtSymbol(t1!.LegalActions.First(a => a.Metadata?["entityId"] == p2Conduits[0].ToString()).Label));
        var t2 = run.WaitPending();
        run.Submit(new RtSymbol(t2!.LegalActions.First(a => a.Metadata?["entityId"] == p2Conduits[1].ToString()).Label));

        Assert.Null(run.WaitPending());
        Assert.True(run.State.GameOver);

        var gameEnd = events.FirstOrDefault(e => e.TypeName == "GameEnd");
        Assert.NotNull(gameEnd);
        Assert.Equal(new RtEntityRef(p2), gameEnd!.Fields["loser"]);
        Assert.Equal(new RtEntityRef(p1), gameEnd.Fields["winner"]);
        Assert.Equal(new RtSymbol("TwoConduitsLost"), gameEnd.Fields["reason"]);
    }

    [Fact]
    public void SingleConduitCollapse_DoesNotEndGame()
    {
        var file = LoadFixture();
        var (run, events) = StartWithHand(file);
        using var _ = run;

        var priority = run.WaitPending();
        run.Submit(new RtSymbol(priority!.LegalActions.First(a => a.Kind == "play_card").Label));

        int p2 = run.State.NamedEntities["Player2"].Id;
        var p2Conduits = run.State.Entities.Values
            .Where(e => e.Kind == "Conduit" && e.OwnerId == p2)
            .Select(e => e.Id)
            .ToList();
        int p1 = run.State.NamedEntities["Player1"].Id;
        var p1Conduits = run.State.Entities.Values
            .Where(e => e.Kind == "Conduit" && e.OwnerId == p1)
            .Select(e => e.Id)
            .ToList();

        // Collapse one P2 conduit and one P1 conduit. No owner hits 2.
        var t1 = run.WaitPending();
        run.Submit(new RtSymbol(t1!.LegalActions.First(a => a.Metadata?["entityId"] == p2Conduits[0].ToString()).Label));
        var t2 = run.WaitPending();
        run.Submit(new RtSymbol(t2!.LegalActions.First(a => a.Metadata?["entityId"] == p1Conduits[0].ToString()).Label));

        Assert.Null(run.WaitPending());
        Assert.False(run.State.GameOver);

        Assert.Equal(2, events.Count(e => e.TypeName == "ConduitCollapsed"));
        Assert.DoesNotContain(events, e => e.TypeName == "GameEnd");
    }
}
