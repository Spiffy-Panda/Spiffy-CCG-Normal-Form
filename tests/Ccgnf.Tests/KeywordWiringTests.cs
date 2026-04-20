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

    // -----------------------------------------------------------------------
    // Fortify N — §2.2 Sub-step B: +N Ramparts while controller's Conduit in
    // this Arena is at integrity ≥ 4. Suppressed (not destroyed) when the
    // Conduit drops below 4; returns if healed back.
    // -----------------------------------------------------------------------

    [Fact]
    public void Fortify_RecordsNumericParamOnRuntimeUnit()
    {
        var file = LoadClashSentinelFixture();
        using var run = NewInterpreter().StartRun(
            file, WithDecks(new[] { "HeavyStriker" }, new[] { "FortifyWall" }));

        // P2 plays FortifyWall Left.
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        // P1 passes.
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));
        Assert.Null(run.WaitPending());

        var p2 = run.State.NamedEntities["Player2"];
        var wall = run.State.Entities[p2.Zones["Battlefield"].Contents.Single()];
        Assert.True(KeywordRuntime.HasKeyword(wall, "Fortify"));
        Assert.Equal(2, KeywordRuntime.GetFortifyAmount(wall));
    }

    [Fact]
    public void Fortify_EffectiveRamparts_FollowsConduitIntegrityThreshold()
    {
        var file = LoadClashSentinelFixture();
        using var run = NewInterpreter().StartRun(
            file, WithDecks(new[] { "HeavyStriker" }, new[] { "FortifyWall" }));

        // P2 plays FortifyWall Left; P1 passes; run drains.
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));
        Assert.Null(run.WaitPending());

        var state = run.State;
        var p2 = state.NamedEntities["Player2"];
        var wall = state.Entities[p2.Zones["Battlefield"].Contents.Single()];
        Assert.Equal("FortifyWall", wall.DisplayName);
        Assert.Equal(3, wall.Counters["current_ramparts"]);

        // P2's Conduit in Left starts at integrity 7 (fixture). Fortify(2)
        // is active → base 3 + 2 = 5.
        var p2LeftConduit = state.Entities.Values.Single(e =>
            e.Kind == "Conduit" && e.OwnerId == p2.Id &&
            e.Parameters.TryGetValue("arena", out var a) &&
            a is RtSymbol s && s.Name == "Left");
        Assert.Equal(7, p2LeftConduit.Counters["integrity"]);
        Assert.Equal(5, KeywordRuntime.EffectiveRamparts(wall, state));

        // Drop the Conduit below the Fortify threshold: bonus suppresses.
        p2LeftConduit.Counters["integrity"] = 3;
        Assert.Equal(3, KeywordRuntime.EffectiveRamparts(wall, state));

        // Heal back to exactly the threshold: bonus returns.
        p2LeftConduit.Counters["integrity"] = 4;
        Assert.Equal(5, KeywordRuntime.EffectiveRamparts(wall, state));
    }

    [Fact]
    public void Fortify_ClashFortification_TracksEffectiveRamparts()
    {
        var file = LoadClashSentinelFixture();
        using var run = NewInterpreter().StartRun(
            file, WithDecks(new[] { "HeavyStriker" }, new[] { "FortifyWall" }));

        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));
        Assert.Null(run.WaitPending());

        var p2 = run.State.NamedEntities["Player2"];
        var wall = run.State.Entities[p2.Zones["Battlefield"].Contents.Single()];

        // FortifyWall has no Sentinel, so projected_force == force == 2 and
        // fortification == effective_ramparts (base 3 + fortify 2 while
        // Conduit healthy).
        Assert.Equal(2, KeywordRuntime.GetClashProjectedForce(wall, run.State));
        Assert.Equal(5, KeywordRuntime.GetClashFortification(wall, run.State));
    }

    // -----------------------------------------------------------------------
    // Triggered-on-Unit dispatch — guide §3.2 wave 2.
    //   DebtPinger's `Triggered(on: Event.EnterPlay(target=self),
    //   effect: SetCounter(Player2, debt, 42))` fires when the Unit lands.
    //   The observable side-effect is Player2.debt flipping 0 → 42.
    // -----------------------------------------------------------------------

    [Fact]
    public void TriggeredOnUnit_OnEnterFires_WhenUnitEntersBattlefield()
    {
        var file = LoadClashSentinelFixture();
        using var run = NewInterpreter().StartRun(
            file, WithDecks(new[] { "DebtPinger" }, Array.Empty<string>()));

        // P2 has no cards; pass.
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));

        // Before DebtPinger enters play, Player2.debt sits at the fixture
        // default (0). Snapshot it, then play the pinger and let the event
        // loop dispatch EnterPlay.
        var player2 = run.State.NamedEntities["Player2"];
        int debtBefore = player2.Counters.GetValueOrDefault("debt", 0);
        Assert.Equal(0, debtBefore);

        // P1 plays DebtPinger; arena pick; Clash hold (no damage).
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("hold"));
        Assert.Null(run.WaitPending());

        // EnterPlay was dispatched during the event loop; the Triggered
        // ability on DebtPinger fired, setting Player2.debt = 42.
        Assert.Equal(42, run.State.NamedEntities["Player2"].Counters["debt"]);
    }

    [Fact]
    public void TriggeredOnUnit_EnterPlayEvent_IsEmittedForEachUnitPlaced()
    {
        var file = LoadClashSentinelFixture();
        var seen = new List<GameEvent>();
        var opts = WithDecks(new[] { "HeavyStriker" }, Array.Empty<string>());
        opts.OnEvent = (e, _) => seen.Add(e);
        using var run = NewInterpreter().StartRun(file, opts);

        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("hold"));
        Assert.Null(run.WaitPending());

        var enterPlay = Assert.Single(seen, e => e.TypeName == "EnterPlay");
        Assert.True(enterPlay.Fields.TryGetValue("target", out var targetVal));
        Assert.IsType<RtEntityRef>(targetVal);
    }

    [Fact]
    public void TriggeredOnUnit_OnEnterShorthand_ExpandsAndFires()
    {
        var file = LoadClashSentinelFixture();
        using var run = NewInterpreter().StartRun(
            file, WithDecks(new[] { "OnEnterShorthandPinger" }, Array.Empty<string>()));

        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("hold"));
        Assert.Null(run.WaitPending());

        Assert.Equal(7, run.State.NamedEntities["Player2"].Counters["debt"]);
    }

    [Fact]
    public void TriggeredOnUnit_EndOfClashShorthand_FiresAfterClashResolves()
    {
        var file = LoadClashSentinelFixture();
        using var run = NewInterpreter().StartRun(
            file, WithDecks(new[] { "EndOfClashPinger" }, Array.Empty<string>()));

        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("hold"));
        Assert.Null(run.WaitPending());

        // PhaseEnd(phase=Clash) fires after the arena loop; the EndOfClash
        // trigger on the Pinger catches it and rewrites debt to 11.
        Assert.Equal(11, run.State.NamedEntities["Player2"].Counters["debt"]);
    }

    [Fact]
    public void Mend_Keyword_HealsOwnersArenaConduitOnEnter()
    {
        var file = LoadClashSentinelFixture();
        using var run = NewInterpreter().StartRun(
            file, WithDecks(new[] { "MendPinger" }, Array.Empty<string>()));

        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));

        // Before playing: drop P1's Left conduit to 3 so a Mend 2 heal
        // moves it to 5 (below the starting_integrity cap of 7).
        int p1Id = run.State.NamedEntities["Player1"].Id;
        var p1LeftConduit = run.State.Entities.Values.Single(e =>
            e.Kind == "Conduit" && e.OwnerId == p1Id &&
            e.Parameters.TryGetValue("arena", out var a) &&
            a is RtSymbol s && s.Name == "Left");
        p1LeftConduit.Counters["integrity"] = 3;

        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("hold"));
        Assert.Null(run.WaitPending());

        Assert.Equal(5, p1LeftConduit.Counters["integrity"]);
    }

    [Fact]
    public void Mend_Keyword_CapsAtStartingIntegrity()
    {
        var file = LoadClashSentinelFixture();
        using var run = NewInterpreter().StartRun(
            file, WithDecks(new[] { "MendPinger" }, Array.Empty<string>()));

        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("pass"));
        // Conduit starts at 7, which is also the default cap — Mend 2 heal
        // should be clamped to 7, not 9.
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("hold"));
        Assert.Null(run.WaitPending());

        int p1Id = run.State.NamedEntities["Player1"].Id;
        var p1LeftConduit = run.State.Entities.Values.Single(e =>
            e.Kind == "Conduit" && e.OwnerId == p1Id &&
            e.Parameters.TryGetValue("arena", out var a) &&
            a is RtSymbol s && s.Name == "Left");
        Assert.Equal(7, p1LeftConduit.Counters["integrity"]);
    }

    // -----------------------------------------------------------------------
    // Sub-step C — per-Arena incoming formula.
    //   incoming[defender] = max(0, projected_force[attacker]
    //                           - fortification[defender])
    // Replaces the per-attacker raw-Force loop in ResolveClashPhase.
    // -----------------------------------------------------------------------

    [Fact]
    public void Clash_SentinelDefender_AbsorbsForceIntoFortification_ConduitUntouched()
    {
        var file = LoadClashSentinelFixture();
        using var run = NewInterpreter().StartRun(
            file, WithDecks(new[] { "HeavyStriker" }, new[] { "SentinelWall" }));

        // P2 plays SentinelWall into Left: fortification = force(3) + ramparts(2) = 5.
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        // P1 plays HeavyStriker into Left: projected_force = 4.
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        // Clash prompt for HeavyStriker: attack.
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("attack"));
        Assert.Null(run.WaitPending());

        // incoming = max(0, 4 - 5) = 0. Player2's Left conduit stays at 7.
        int p2Id = run.State.NamedEntities["Player2"].Id;
        var p2LeftConduit = run.State.Entities.Values.Single(e =>
            e.Kind == "Conduit" && e.OwnerId == p2Id &&
            e.Parameters.TryGetValue("arena", out var a) &&
            a is RtSymbol s && s.Name == "Left");
        Assert.Equal(7, p2LeftConduit.Counters["integrity"]);
    }

    [Fact]
    public void Clash_DamageDealtEvent_CarriesFortificationAndProjectedForce()
    {
        var file = LoadClashSentinelFixture();
        var seen = new List<GameEvent>();
        var opts = WithDecks(new[] { "HeavyStriker" }, new[] { "FortifyWall" });
        opts.OnEvent = (e, _) => seen.Add(e);
        using var run = NewInterpreter().StartRun(file, opts);

        // P2 plays FortifyWall Left: force 2, effective ramparts 5, fortification 5.
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        // P1 plays HeavyStriker Left, attacks.
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Kind == "play_card")));
        run.Submit(new RtSymbol(PickLabel(run.WaitPending(), a => a.Metadata?["pos"] == "Left")));
        Assert.NotNull(run.WaitPending());
        run.Submit(new RtSymbol("attack"));
        Assert.Null(run.WaitPending());

        // Fortify(2) suppresses HeavyStriker's 4 Force: incoming = max(0, 4 - 5) = 0.
        int p2Id = run.State.NamedEntities["Player2"].Id;
        var p2LeftConduit = run.State.Entities.Values.Single(e =>
            e.Kind == "Conduit" && e.OwnerId == p2Id &&
            e.Parameters.TryGetValue("arena", out var a) &&
            a is RtSymbol s && s.Name == "Left");
        Assert.Equal(7, p2LeftConduit.Counters["integrity"]);

        // No DamageDealt event was emitted at all when incoming is 0 — the
        // new formula skips the zero-damage beat to avoid false triggers.
        Assert.DoesNotContain(seen, e => e.TypeName == "DamageDealt");
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
