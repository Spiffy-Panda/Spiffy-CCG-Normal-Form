using Ccgnf.Ast;
using Ccgnf.Interpreter;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using InterpreterRt = Ccgnf.Interpreter.Interpreter;

namespace Ccgnf.Tests;

/// <summary>
/// Single accumulator for "is keyword X actually wired end-to-end?" probes
/// (see <c>docs/plan/engine-completion-guide.md</c> §3.1). Each test drives
/// the interpreter to a point where the keyword would fire and asserts the
/// observable side-effect. A failing probe means the keyword is declared in
/// the encoding but dead at runtime; a passing probe means the wiring reached
/// the Clash / SBA / event code path the card text implies.
/// </summary>
public class KeywordWiringTests
{
    private static AstFile LoadClashSentinelFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "clash-sentinel.ccgnf");
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

    private static InterpreterOptions WithDecks(
        IReadOnlyList<string> p1Deck,
        IReadOnlyList<string> p2Deck) => new()
    {
        Seed = 1,
        InitialDecks = new IReadOnlyList<string>?[] { p1Deck, p2Deck },
    };

    // -----------------------------------------------------------------------
    // Sentinel — §2.1: projects 0 Force, adds force into fortification.
    // -----------------------------------------------------------------------

    private static string PickLabel(InputRequest? req, Func<LegalAction, bool> pred)
    {
        Assert.NotNull(req);
        return req!.LegalActions.First(pred).Label;
    }

    [Fact]
    public void Sentinel_IsRecordedAsKeywordOnRuntimeUnit()
    {
        var file = LoadClashSentinelFixture();
        using var run = NewInterpreter().StartRun(
            file, WithDecks(new[] { "HeavyStriker" }, new[] { "SentinelWall" }));

        // Player2's Main Phase first: play SentinelWall into Left.
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));

        // Player1's Main Phase: pass.
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));

        // No attackers for Player1 (its only card was never played) → no
        // Clash prompt; the run drains.
        Assert.Null(run.WaitPending());

        var p2 = run.State.NamedEntities["Player2"];
        var sentinelId = p2.Zones["Battlefield"].Contents.Single();
        var sentinel = run.State.Entities[sentinelId];

        Assert.True(KeywordRuntime.HasKeyword(sentinel, "Sentinel"));
    }

    [Fact]
    public void Sentinel_ClashHelpers_ProjectZeroAndAddForceToFortification()
    {
        var file = LoadClashSentinelFixture();
        using var run = NewInterpreter().StartRun(
            file, WithDecks(new[] { "HeavyStriker" }, new[] { "SentinelWall" }));

        // P2 plays SentinelWall Left.
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        // P1 Main: pass (keeps HeavyStriker out of play — we only want to
        // probe the SentinelWall that's already on the battlefield).
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));
        Assert.Null(run.WaitPending());

        var p2 = run.State.NamedEntities["Player2"];
        var sentinel = run.State.Entities[p2.Zones["Battlefield"].Contents.Single()];
        Assert.Equal("SentinelWall", sentinel.DisplayName);

        // Sentinel's Static rule: 0 projected, force + ramparts into fortification.
        Assert.Equal(0, KeywordRuntime.GetClashProjectedForce(sentinel, run.State));
        // force (3) + current_ramparts (2) = 5.
        Assert.Equal(5, KeywordRuntime.GetClashFortification(sentinel, run.State));
    }

    [Fact]
    public void VanillaUnit_ClashHelpers_ReturnForceAndRampartsUnchanged()
    {
        var file = LoadClashSentinelFixture();
        using var run = NewInterpreter().StartRun(
            file, WithDecks(new[] { "HeavyStriker" }, Array.Empty<string>()));

        // P2 Main: pass (no cards anyway).
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));
        // P1 Main: play HeavyStriker Left.
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        // Clash: hold (we only want stats, not damage).
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("hold"));
        Assert.Null(run.WaitPending());

        var p1 = run.State.NamedEntities["Player1"];
        var striker = run.State.Entities[p1.Zones["Battlefield"].Contents.Single()];
        Assert.Equal("HeavyStriker", striker.DisplayName);

        Assert.False(KeywordRuntime.HasKeyword(striker, "Sentinel"));
        Assert.Equal(4, KeywordRuntime.GetClashProjectedForce(striker, run.State));
        Assert.Equal(1, KeywordRuntime.GetClashFortification(striker, run.State));
    }
}
