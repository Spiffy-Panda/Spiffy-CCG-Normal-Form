using Ccgnf.Ast;

namespace Ccgnf.Bots.Bench;

/// <summary>
/// Round-robin tournament harness. For a given deck, plays
/// <c>GamesPerEntry</c> mirror matches for every bot profile in
/// <see cref="TournamentConfig.Entries"/>. Optional
/// <see cref="TournamentConfig.ExtraPairs"/> let the caller include
/// cross-deck baseline comparisons (see §10.2h).
/// </summary>
public sealed class TournamentRunner
{
    public delegate IRoomBot BotBuilder();

    public sealed record Entry(string Name, BotBuilder Build);

    public sealed record Pair(string DeckId, string BotName, IReadOnlyList<string>? Cards, BotBuilder Build);

    public sealed record TournamentConfig(
        AstFile File,
        string DeckId,
        IReadOnlyList<string>? Cards,
        IReadOnlyList<Entry> Entries,
        IReadOnlyList<Pair>? ExtraPairs = null,
        int GamesPerEntry = 20,
        int BaseSeed = 0,
        int MaxInputsPerGame = 5_000,
        int MaxEventsPerGame = 10_000);

    public event Action<TournamentProgress>? OnProgress;

    public TournamentResult Run(TournamentConfig cfg, CancellationToken ct = default)
    {
        var rows = new List<TournamentRow>();

        foreach (var entry in cfg.Entries)
        {
            if (ct.IsCancellationRequested) break;
            rows.Add(RunOnePair(cfg.File, cfg.DeckId, cfg.Cards, entry.Name,
                entry.Build, entry.Build, cfg.GamesPerEntry, cfg.BaseSeed,
                cfg.MaxInputsPerGame, cfg.MaxEventsPerGame, ct));
        }

        if (cfg.ExtraPairs is not null)
        {
            foreach (var pair in cfg.ExtraPairs)
            {
                if (ct.IsCancellationRequested) break;
                rows.Add(RunOnePair(cfg.File, pair.DeckId, pair.Cards, pair.BotName,
                    pair.Build, pair.Build, cfg.GamesPerEntry, cfg.BaseSeed,
                    cfg.MaxInputsPerGame, cfg.MaxEventsPerGame, ct));
            }
        }

        rows.Sort((a, b) => b.WinRate.CompareTo(a.WinRate));
        return new TournamentResult(cfg.DeckId, rows);
    }

    private TournamentRow RunOnePair(
        AstFile file,
        string deckId,
        IReadOnlyList<string>? deck,
        string botName,
        BotBuilder a,
        BotBuilder b,
        int games,
        int baseSeed,
        int maxInputs,
        int maxEvents,
        CancellationToken ct)
    {
        int wins = 0, losses = 0, draws = 0;
        long totalSteps = 0;

        for (int i = 0; i < games; i++)
        {
            if (ct.IsCancellationRequested) break;

            var result = BotMatchRunner.RunMatch(
                file, deck, deck, a(), b(), baseSeed + i, maxEvents, maxInputs, ct);

            switch (result.Outcome)
            {
                case MatchOutcome.ABWins: wins++; break;
                case MatchOutcome.BBWins: losses++; break;
                default: draws++; break;
            }
            totalSteps += result.StepCount;

            OnProgress?.Invoke(new TournamentProgress(
                deckId, botName, i + 1, games, wins, losses, draws));
        }

        int completed = wins + losses + draws;
        float winRate = completed == 0 ? 0f : (float)wins / completed;
        float avgSteps = completed == 0 ? 0f : (float)totalSteps / completed;

        return new TournamentRow(deckId, botName, games, wins, losses, draws, winRate, avgSteps);
    }
}

public sealed record TournamentRow(
    string DeckId,
    string BotName,
    int Games,
    int Wins,
    int Losses,
    int Draws,
    float WinRate,
    float AvgSteps);

public sealed record TournamentResult(
    string DeckId,
    IReadOnlyList<TournamentRow> Rows);

public sealed record TournamentProgress(
    string DeckId,
    string BotName,
    int Completed,
    int Total,
    int Wins,
    int Losses,
    int Draws);
