using Ccgnf.Ast;
using Ccgnf.Interpreter;
using InterpreterRt = Ccgnf.Interpreter.Interpreter;

namespace Ccgnf.Bots.Bench;

/// <summary>
/// Headless runner for a single match between two bots. Given an
/// <see cref="AstFile"/> (the validated encoding), per-player decks, and
/// two <see cref="IRoomBot"/>s, drives the interpreter to a terminal
/// state and returns the outcome.
/// <para>
/// Used by both the benchmark harness and the <c>/api/ai/tournament</c>
/// endpoint — see §10.2h + §10.2i in the plan. Terminates via
/// <see cref="InterpreterOptions.MaxEventDispatches"/> or the natural
/// GameEnd flow.
/// </para>
/// </summary>
public static class BotMatchRunner
{
    public static MatchResult RunMatch(
        AstFile file,
        IReadOnlyList<string>? deckA,
        IReadOnlyList<string>? deckB,
        IRoomBot botA,
        IRoomBot botB,
        int seed = 0,
        int maxEvents = 10_000,
        int maxInputs = 5_000,
        CancellationToken ct = default)
    {
        var interpreter = new InterpreterRt();
        using var run = interpreter.StartRun(file, new InterpreterOptions
        {
            Seed = seed,
            InitialDecks = new[] { deckA, deckB },
            MaxEventDispatches = maxEvents,
        });

        int inputsServed = 0;
        while (true)
        {
            if (ct.IsCancellationRequested) { run.Stop(); break; }

            var pending = run.WaitPending(ct);
            if (pending is null) break;
            if (inputsServed++ >= maxInputs)
            {
                run.Stop();
                break;
            }

            int cpuId = pending.PlayerId ?? run.State.Players[0].Id;
            IRoomBot bot = cpuId == run.State.Players[0].Id ? botA : botB;
            var pick = bot.Choose(run.State, pending, cpuId);
            run.Submit(pick);
        }

        int winnerSeat = DetermineWinnerSeat(run.State);
        return new MatchResult(
            Outcome: winnerSeat switch
            {
                0 => MatchOutcome.ABWins,
                1 => MatchOutcome.BBWins,
                _ => MatchOutcome.Draw,
            },
            WinnerSeat: winnerSeat,
            StepCount: run.State.StepCount,
            InputsServed: inputsServed,
            RunStatus: run.Status);
    }

    /// <summary>
    /// Seat index of the winner (0 = first player / botA, 1 = second /
    /// botB); -1 when the game didn't resolve cleanly. Determined by
    /// standing-conduit count — whichever seat has more standing Conduits
    /// owned wins. Ties return -1.
    /// </summary>
    internal static int DetermineWinnerSeat(GameState state)
    {
        if (state.Players.Count < 2) return -1;
        var counts = new int[state.Players.Count];
        for (int i = 0; i < state.Players.Count; i++)
        {
            int playerId = state.Players[i].Id;
            foreach (var e in state.Entities.Values)
            {
                if (e.Kind != "Conduit") continue;
                if (e.OwnerId != playerId) continue;
                if (e.Tags.Contains("collapsed")) continue;
                counts[i]++;
            }
        }

        int max = counts.Max();
        int winners = counts.Count(c => c == max);
        if (winners != 1) return -1;
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] == max) return i;
        return -1;
    }
}

public enum MatchOutcome
{
    ABWins,
    BBWins,
    Draw,
}

public sealed record MatchResult(
    MatchOutcome Outcome,
    int WinnerSeat,
    long StepCount,
    int InputsServed,
    RunStatus RunStatus);
