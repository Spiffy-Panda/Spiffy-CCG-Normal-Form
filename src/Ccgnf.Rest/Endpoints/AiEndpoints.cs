using System.Text.Json;
using Ccgnf.Bots;
using Ccgnf.Bots.Bench;
using Ccgnf.Bots.Utility;
using Ccgnf.Interpreter;
using Ccgnf.Rest.Serialization;
using Ccgnf.Rest.Services;

namespace Ccgnf.Rest.Endpoints;

/// <summary>
/// AI-editor + tournament endpoints, landing with step 10.2h. All write
/// paths (PUT weights, PUT phase-bt, POST tournament) are gated behind
/// <c>CCGNF_AI_EDITOR=1</c> so production deployments don't expose them.
/// </summary>
internal static class AiEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/ai/bots", GetBots);
        app.MapGet("/api/ai/weights", GetWeights);
        app.MapPut("/api/ai/weights", PutWeights);
        app.MapPost("/api/ai/preview-score", PreviewScore);
        app.MapPost("/api/ai/tournament", Tournament);
        app.MapPost("/api/ai/tournament/run", RunTournamentV2);
        app.MapPost("/api/ai/tournament/validate", ValidateTournamentConfig);
        app.MapGet("/api/ai/tournament/logs", ListTournamentLogs);
        app.MapGet("/api/ai/tournament/logs/{id}", GetTournamentLog);
    }

    private static bool EditorOn =>
        string.Equals(Environment.GetEnvironmentVariable("CCGNF_AI_EDITOR"), "1", StringComparison.Ordinal);

    // ─── GET /api/ai/bots ──────────────────────────────────────────────

    private static IResult GetBots()
    {
        var bots = new List<BotProfileDto>
        {
            new("fixed", "Fixed Ladder", "Baseline ladder policy."),
            new("utility", "Utility (default)", "Utility bot with default weights + phase BT."),
        };
        bots.AddRange(ScanExperimentalBots());
        return Results.Ok(bots);
    }

    /// <summary>
    /// Enumerate <c>encoding/ai/experimental/*/weights.json</c> and project
    /// each one as a <see cref="BotProfileDto"/> with id
    /// <c>experimental/&lt;slug&gt;</c>. Skips directories whose weights
    /// file is missing or malformed so a bad experiment never breaks
    /// the <c>GET /api/ai/bots</c> call.
    /// </summary>
    private static IEnumerable<BotProfileDto> ScanExperimentalBots()
    {
        string dir;
        try { dir = ExperimentalDir(); }
        catch { yield break; }

        if (!Directory.Exists(dir)) yield break;

        foreach (var sub in Directory.EnumerateDirectories(dir).OrderBy(p => p, StringComparer.Ordinal))
        {
            var weightsPath = Path.Combine(sub, "weights.json");
            if (!File.Exists(weightsPath)) continue;

            // Parse lazily — a malformed weights file shouldn't crash the
            // listing, but we do want to exclude it so the web dropdown
            // never offers something the tournament runner will drop.
            try { WeightTable.FromJson(File.ReadAllText(weightsPath)); }
            catch (WeightTableFormatException) { continue; }

            var slug = Path.GetFileName(sub);
            var description = ReadFirstParagraph(Path.Combine(sub, "notes.md"))
                ?? "Experimental weight profile.";

            yield return new BotProfileDto(
                Id: $"experimental/{slug}",
                Name: slug,
                Description: description);
        }
    }

    /// <summary>
    /// Pull the first non-empty, non-heading paragraph from a markdown
    /// file as the bot's one-line description. Bounded to ~160 chars so
    /// the web dropdown can render it inline.
    /// </summary>
    private static string? ReadFirstParagraph(string path)
    {
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path);
        foreach (var block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = block.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("#")) continue;
            var flat = trimmed.Replace('\n', ' ').Replace('\r', ' ');
            return flat.Length > 160 ? flat[..157] + "..." : flat;
        }
        return null;
    }

    // ─── GET /api/ai/weights ───────────────────────────────────────────

    private static IResult GetWeights()
    {
        var path = WeightsPath();
        if (!File.Exists(path))
        {
            return Results.Ok(new AiWeightsDto(
                Source: "default",
                Path: null,
                ConsiderationKeys: DefaultConsiderations.Keys().ToArray(),
                Json: "",
                EditorEnabled: EditorOn));
        }
        var json = File.ReadAllText(path);
        return Results.Ok(new AiWeightsDto(
            Source: "file",
            Path: path,
            ConsiderationKeys: DefaultConsiderations.Keys().ToArray(),
            Json: json,
            EditorEnabled: EditorOn));
    }

    // ─── PUT /api/ai/weights ───────────────────────────────────────────

    private static IResult PutWeights(AiWeightsWriteRequest req)
    {
        if (!EditorOn)
            return Results.NotFound(new { error = "AI editor disabled (set CCGNF_AI_EDITOR=1 to enable)." });
        if (string.IsNullOrWhiteSpace(req.Json))
            return Results.BadRequest(new { error = "Missing 'json' body." });

        try
        {
            // Validate by parsing — reject before touching disk.
            WeightTable.FromJson(req.Json);
        }
        catch (WeightTableFormatException ex)
        {
            return Results.BadRequest(new
            {
                error = "Invalid weights JSON",
                details = ex.Message,
                allowedConsiderationKeys = DefaultConsiderations.Keys(),
            });
        }

        var path = WeightsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, req.Json);
        return Results.Ok(new { ok = true, path });
    }

    // ─── POST /api/ai/preview-score ────────────────────────────────────

    /// <summary>
    /// Re-score a supplied list of legal actions under the supplied
    /// weights. Used by the <c>#/ai</c> editor's decision-replay panel.
    /// Read-only; does not touch disk.
    /// </summary>
    private static IResult PreviewScore(AiPreviewScoreRequest req)
    {
        // Only usable when the editor is on — preview scoring reveals
        // enough internals that gating it with the rest of the editor
        // surface is a safe default.
        if (!EditorOn)
            return Results.NotFound(new { error = "AI editor disabled." });

        WeightTable table;
        try
        {
            table = string.IsNullOrWhiteSpace(req.WeightsJson)
                ? WeightTable.Uniform(DefaultConsiderations.Keys())
                : WeightTable.FromJson(req.WeightsJson);
        }
        catch (WeightTableFormatException ex)
        {
            return Results.BadRequest(new { error = "Invalid weights JSON", details = ex.Message });
        }

        // Note: this endpoint scores *standalone* actions without a live
        // GameState. Considerations that need the live state (conduit
        // softness, overlap) return the neutral value on the synthetic
        // context — the caller should rely on the tabletop-anchored
        // scoring flow for full fidelity. This endpoint is intended for
        // quick dial tuning against pre-captured "chosen cost" rows.
        var considerations = DefaultConsiderations.All();
        var state = BuildSyntheticState(req.CpuAether);

        var bot = new UtilityBot(considerations, table);
        var legalActions = (req.LegalActions ?? Array.Empty<AiPreviewAction>())
            .Select(la => new Ccgnf.Interpreter.LegalAction(
                la.Kind ?? "",
                la.Label ?? "",
                la.Metadata ?? new Dictionary<string, string>()))
            .ToArray();
        var pending = new Ccgnf.Interpreter.InputRequest("preview", state.Players[0].Id, legalActions);
        var ranked = bot.ScoreAll(state, pending, state.Players[0].Id);

        var rows = ranked.Select(r => new AiPreviewRow(
            Kind: r.Action.Kind,
            Label: r.Action.Label,
            Score: r.Score,
            Breakdown: r.Breakdown)).ToArray();
        return Results.Ok(new { rows });
    }

    private static Ccgnf.Interpreter.GameState BuildSyntheticState(int cpuAether)
    {
        var state = new Ccgnf.Interpreter.GameState();
        var cpu = state.AllocateEntity("Player", "Preview-CPU");
        var opp = state.AllocateEntity("Player", "Preview-Opponent");
        state.Players.Add(cpu);
        state.Players.Add(opp);
        cpu.Counters["aether"] = cpuAether;
        return state;
    }

    // ─── POST /api/ai/tournament ───────────────────────────────────────

    private static IResult Tournament(
        AiTournamentRequest req,
        ProjectCatalog projects,
        DeckCatalog decks)
    {
        if (!EditorOn)
            return Results.NotFound(new { error = "AI editor disabled." });
        if (string.IsNullOrWhiteSpace(req.DeckId))
            return Results.BadRequest(new { error = "Missing 'deckId'." });

        var allDecks = decks.Get();
        var deck = allDecks.FirstOrDefault(d =>
            string.Equals(d.Id, req.DeckId, StringComparison.OrdinalIgnoreCase));
        if (deck is null)
            return Results.NotFound(new { error = $"Deck '{req.DeckId}' not found." });

        var cards = ExpandDeck(deck);

        var snapshot = projects.Get();
        var file = snapshot.File
            ?? throw new InvalidOperationException("ProjectCatalog has no AST file loaded.");

        var entries = BuildEntries(req.Bots);
        var config = new TournamentRunner.TournamentConfig(
            File: file,
            DeckId: deck.Id,
            Cards: cards,
            Entries: entries,
            GamesPerEntry: req.Games > 0 ? req.Games : 10,
            BaseSeed: req.Seed,
            MaxInputsPerGame: req.MaxInputsPerGame > 0 ? req.MaxInputsPerGame : 2_000,
            MaxEventsPerGame: req.MaxEventsPerGame > 0 ? req.MaxEventsPerGame : 50_000);

        var runner = new TournamentRunner();
        // v1 blocking mode: run synchronously, return final leaderboard.
        // Streaming via SSE is a follow-up — the plan notes it as a UI
        // concern; today we report only the rolled-up result.
        var result = runner.Run(config);

        return Results.Ok(new AiTournamentResponse(
            DeckId: result.DeckId,
            Rows: result.Rows.Select(r => new AiTournamentRow(
                r.BotName, r.Games, r.Wins, r.Losses, r.Draws, r.WinRate, r.AvgSteps)).ToArray(),
            Timestamp: DateTime.UtcNow.ToString("o")));
    }

    private static IReadOnlyList<string> ExpandDeck(PresetDeckDto deck)
    {
        var cards = new List<string>(deck.CardCount);
        foreach (var c in deck.Cards)
            for (int i = 0; i < c.Count; i++)
                cards.Add(c.Name);
        return cards;
    }

    private static IReadOnlyList<TournamentRunner.Entry> BuildEntries(IReadOnlyList<string>? names)
    {
        var requested = names is { Count: > 0 }
            ? names
            : new[] { "fixed", "utility" };

        var entries = new List<TournamentRunner.Entry>();
        foreach (var name in requested)
        {
            if (name.StartsWith("experimental/", StringComparison.Ordinal))
            {
                var slug = name.Substring("experimental/".Length);
                var path = Path.Combine(FindRepoRoot(), "encoding", "ai",
                                        "experimental", slug, "weights.json");
                if (!File.Exists(path)) continue;
                WeightTable weights;
                try
                {
                    weights = WeightTable.FromJson(File.ReadAllText(path));
                }
                catch (WeightTableFormatException)
                {
                    continue;
                }
                entries.Add(new TournamentRunner.Entry(name,
                    () => UtilityBotFactory.Build(
                        weights: weights, memory: new PhaseMemory())));
                continue;
            }

            TournamentRunner.BotBuilder? builder = name switch
            {
                "fixed" => () => new FixedLadderBot(),
                "utility" => () => UtilityBotFactory.Build(memory: new PhaseMemory()),
                _ => null,
            };
            if (builder is null) continue;
            entries.Add(new TournamentRunner.Entry(name, builder));
        }
        return entries;
    }

    // ─── Helpers ───────────────────────────────────────────────────────

    private static string WeightsPath()
    {
        string projectRootName = Environment.GetEnvironmentVariable("CCGNF_PROJECT_ROOT") ?? "encoding";
        return Path.Combine(FindRepoRoot(), projectRootName, "ai", "utility-weights.json");
    }

    private static string ExperimentalDir()
    {
        string projectRootName = Environment.GetEnvironmentVariable("CCGNF_PROJECT_ROOT") ?? "encoding";
        return Path.Combine(FindRepoRoot(), projectRootName, "ai", "experimental");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ccgnf.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Cannot find repo root.");
    }

    // ─── POST /api/ai/tournament/validate ──────────────────────────────

    /// <summary>
    /// Read-only validation of a tournament config: resolves every pair's
    /// deck and bot profile, reports unknown references in a structured
    /// response, but never runs a match. Used by the tournament editor to
    /// green-light an imported JSON file before offering a Run button.
    /// </summary>
    private static IResult ValidateTournamentConfig(
        TournamentConfigV2 cfg,
        DeckCatalog decks)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var allDecks = decks.Get();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        if (cfg.Pairs is null || cfg.Pairs.Count < 1)
            errors.Add("Config must declare at least one pair.");

        foreach (var pair in cfg.Pairs ?? Array.Empty<TournamentPairDto>())
        {
            if (string.IsNullOrWhiteSpace(pair.PairId))
                errors.Add("Pair is missing 'pairId'.");
            else if (!seenIds.Add(pair.PairId))
                errors.Add($"Duplicate pairId '{pair.PairId}'.");

            if (allDecks.FirstOrDefault(d => string.Equals(d.Id, pair.DeckId, StringComparison.OrdinalIgnoreCase)) is null)
                errors.Add($"Pair '{pair.PairId}' references unknown deckId '{pair.DeckId}'.");

            if (!BotProfileExists(pair.BotProfile))
                errors.Add($"Pair '{pair.PairId}' references unknown botProfile '{pair.BotProfile}'.");
        }

        foreach (var m in cfg.Matchups ?? Array.Empty<TournamentMatchupDto>())
        {
            if (!seenIds.Contains(m.APairId))
                errors.Add($"Matchup references unknown pair '{m.APairId}'.");
            if (!seenIds.Contains(m.BPairId))
                errors.Add($"Matchup references unknown pair '{m.BPairId}'.");
        }

        if (cfg.GamesPerMatchup <= 0)
            warnings.Add("gamesPerMatchup ≤ 0; a default of 4 will be used at run time.");

        return Results.Ok(new TournamentValidateResponse(
            Ok: errors.Count == 0,
            Errors: errors,
            Warnings: warnings));
    }

    private static bool BotProfileExists(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile)) return false;
        if (profile is "fixed" or "utility") return true;
        if (!profile.StartsWith("experimental/", StringComparison.Ordinal)) return false;
        var slug = profile.Substring("experimental/".Length);
        var path = Path.Combine(FindRepoRoot(), "encoding", "ai", "experimental", slug, "weights.json");
        return File.Exists(path);
    }

    // ─── POST /api/ai/tournament/run ───────────────────────────────────

    /// <summary>
    /// Runs a v2 tournament: a list of (deck, bot) pairs plus either an
    /// explicit matchup list or a round-robin. Returns a per-pair
    /// leaderboard, a matchup-by-matchup breakdown, and a prose-style
    /// analysis summary.
    /// <para>
    /// The request body is the canonical exportable format: the GUI
    /// exports and imports it verbatim, and an LLM driving the REST host
    /// can POST the same JSON directly to kick off a run.
    /// </para>
    /// </summary>
    private static IResult RunTournamentV2(
        TournamentConfigV2 cfg,
        ProjectCatalog projects,
        DeckCatalog decks)
    {
        if (!EditorOn)
            return Results.NotFound(new { error = "AI editor disabled (set CCGNF_AI_EDITOR=1 to enable)." });
        if (cfg.Pairs is null || cfg.Pairs.Count < 1)
            return Results.BadRequest(new { error = "Config must declare at least one pair." });

        var snapshot = projects.Get();
        var file = snapshot.File
            ?? throw new InvalidOperationException("ProjectCatalog has no AST file loaded.");

        var allDecks = decks.Get();
        var resolvedPairs = new List<ResolvedPair>(cfg.Pairs.Count);
        foreach (var p in cfg.Pairs)
        {
            if (string.IsNullOrWhiteSpace(p.PairId))
                return Results.BadRequest(new { error = "Every pair must have a 'pairId'." });

            var deck = allDecks.FirstOrDefault(d =>
                string.Equals(d.Id, p.DeckId, StringComparison.OrdinalIgnoreCase));
            if (deck is null)
                return Results.BadRequest(new { error = $"Pair '{p.PairId}' references unknown deckId '{p.DeckId}'." });

            var builder = BuildBotFactory(p.BotProfile);
            if (builder is null)
                return Results.BadRequest(new { error = $"Pair '{p.PairId}' references unknown botProfile '{p.BotProfile}'." });

            resolvedPairs.Add(new ResolvedPair(
                PairId: p.PairId,
                DeckId: deck.Id,
                Cards: ExpandDeck(deck),
                BotProfile: p.BotProfile,
                Builder: builder));
        }

        // Build matchup list — explicit or round-robin.
        var matchups = BuildMatchupList(cfg, resolvedPairs);
        if (matchups.Count == 0)
            return Results.BadRequest(new { error = "No matchups produced — add pairs or matchups entries." });

        int games = cfg.GamesPerMatchup > 0 ? cfg.GamesPerMatchup : 4;
        int baseSeed = cfg.BaseSeed;
        int maxInputs = cfg.MaxInputsPerGame > 0 ? cfg.MaxInputsPerGame : 2_000;
        int maxEvents = cfg.MaxEventsPerGame > 0 ? cfg.MaxEventsPerGame : 50_000;

        // Run each matchup and aggregate.
        var pairIndex = resolvedPairs.ToDictionary(p => p.PairId, StringComparer.Ordinal);
        var matchResults = new List<TournamentMatchupResultDto>();
        var aggregates = resolvedPairs.ToDictionary(
            p => p.PairId,
            p => new AggregateMut(p.PairId, p.DeckId, p.BotProfile),
            StringComparer.Ordinal);

        int seedCursor = 0;
        foreach (var (aId, bId) in matchups)
        {
            var a = pairIndex[aId];
            var b = pairIndex[bId];
            int aWins = 0, bWins = 0, draws = 0;
            long totalSteps = 0;

            for (int i = 0; i < games; i++)
            {
                var r = BotMatchRunner.RunMatch(
                    file, a.Cards, b.Cards, a.Builder(), b.Builder(),
                    baseSeed + seedCursor + i, maxEvents, maxInputs);
                totalSteps += r.StepCount;
                switch (r.Outcome)
                {
                    case MatchOutcome.ABWins: aWins++; break;
                    case MatchOutcome.BBWins: bWins++; break;
                    default: draws++; break;
                }
            }
            seedCursor += games;

            float avgSteps = games == 0 ? 0f : (float)totalSteps / games;
            float aWinRate = games == 0 ? 0f : (float)aWins / games;

            matchResults.Add(new TournamentMatchupResultDto(
                APairId: aId, BPairId: bId,
                Games: games, AWins: aWins, BWins: bWins, Draws: draws,
                AWinRate: aWinRate, AvgSteps: avgSteps));

            aggregates[aId].Record(wins: aWins, losses: bWins, draws: draws, steps: totalSteps, games: games);
            if (!string.Equals(aId, bId, StringComparison.Ordinal))
                aggregates[bId].Record(wins: bWins, losses: aWins, draws: draws, steps: totalSteps, games: games);
        }

        var pairRows = aggregates.Values
            .Select(a => a.ToDto())
            .OrderByDescending(r => r.WinRate)
            .ThenBy(r => r.AvgSteps)
            .ToArray();

        var analysis = BuildAnalysis(pairRows, matchResults);
        string level = NormalizeLogLevel(cfg.LogLevel);
        var learningLog = level == "llm"
            ? BuildLearningLog(cfg, resolvedPairs, pairRows, matchResults, analysis)
            : Array.Empty<string>();
        string? logPath = level == "llm"
            ? WriteTournamentLog(cfg, pairRows, matchResults, analysis, learningLog)
            : null;

        return Results.Ok(new TournamentRunResponseV2(
            Config: cfg,
            Pairs: pairRows,
            Matchups: matchResults,
            Analysis: analysis,
            Timestamp: DateTime.UtcNow.ToString("o"),
            LogLevel: level,
            LearningLog: learningLog,
            LogPath: logPath));
    }

    private static string NormalizeLogLevel(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "silent" => "silent",
            "llm" => "llm",
            _ => "summary",
        };
    }

    /// <summary>
    /// Produces a short, human-readable natural-language summary of the
    /// tournament — one statement per matchup plus cross-pair observations
    /// — that an LLM can ingest as training signal for "which bot/deck
    /// pairings beat which." The format is deliberately plain prose: no
    /// JSON, no markdown tables, because downstream models parse English
    /// better than structured diffs of score columns.
    /// </summary>
    private static IReadOnlyList<string> BuildLearningLog(
        TournamentConfigV2 cfg,
        IReadOnlyList<ResolvedPair> pairs,
        IReadOnlyList<TournamentPairRowDto> rows,
        IReadOnlyList<TournamentMatchupResultDto> matchups,
        TournamentAnalysisDto analysis)
    {
        var log = new List<string>();
        var pairById = pairs.ToDictionary(p => p.PairId, StringComparer.Ordinal);

        log.Add($"Tournament '{cfg.Name ?? "unnamed"}' ran {analysis.TotalGames} games across " +
                $"{analysis.TotalMatchups} matchup(s). Pairs: " +
                string.Join(", ", pairs.Select(p => $"{p.PairId}=(deck:{p.DeckId},bot:{p.BotProfile})")));

        foreach (var m in matchups)
        {
            var a = pairById[m.APairId];
            var b = pairById[m.BPairId];
            string outcome;
            if (m.AWins > m.BWins)
                outcome = $"{m.APairId} won {m.AWins}/{m.Games} (BWins:{m.BWins},Draws:{m.Draws}, {m.AWinRate * 100:F0}%).";
            else if (m.BWins > m.AWins)
                outcome = $"{m.BPairId} won {m.BWins}/{m.Games} (AWins:{m.AWins},Draws:{m.Draws}, A side {m.AWinRate * 100:F0}%).";
            else
                outcome = $"Tied {m.AWins}-{m.BWins}-{m.Draws} over {m.Games} games.";

            var lesson = InterpretMatchup(a, b, m);
            log.Add($"Matchup {m.APairId} vs {m.BPairId}: "
                + $"deck '{a.DeckId}' piloted by '{a.BotProfile}' versus "
                + $"deck '{b.DeckId}' piloted by '{b.BotProfile}'. "
                + outcome + " " + lesson
                + $" Average game length: {m.AvgSteps:F0} steps.");
        }

        if (analysis.TopPerformer is not null && analysis.WeakestPerformer is not null
            && !string.Equals(analysis.TopPerformer, analysis.WeakestPerformer, StringComparison.Ordinal))
        {
            var top = pairById[analysis.TopPerformer];
            var weak = pairById[analysis.WeakestPerformer];
            log.Add($"Across all matchups, '{top.PairId}' (deck:{top.DeckId},bot:{top.BotProfile}) "
                  + $"outperformed '{weak.PairId}' (deck:{weak.DeckId},bot:{weak.BotProfile}). "
                  + "Pattern: the top pair's bot profile is worth studying as a candidate to pair with similar decks; "
                  + "the weakest pair suggests either a weight-table mismatch or a deck that disagrees with the bot's scoring priorities.");
        }

        // Cross-matchup patterns: when a bot wins on some decks and loses
        // on others, that tells the LLM this bot has *deck affinity*, not
        // universal strength.
        var botOutcomes = rows
            .GroupBy(r => r.BotProfile, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToArray();
        foreach (var g in botOutcomes)
        {
            var best = g.OrderByDescending(r => r.WinRate).First();
            var worst = g.OrderBy(r => r.WinRate).First();
            if (best.WinRate - worst.WinRate >= 0.3f)
            {
                log.Add($"Bot profile '{g.Key}' swings hard by deck: {best.WinRate * 100:F0}% with "
                      + $"'{best.DeckId}' vs {worst.WinRate * 100:F0}% with '{worst.DeckId}'. "
                      + "This bot has deck affinity rather than universal strength — match it to the former, avoid the latter.");
            }
        }

        var deckOutcomes = rows
            .GroupBy(r => r.DeckId, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToArray();
        foreach (var g in deckOutcomes)
        {
            var best = g.OrderByDescending(r => r.WinRate).First();
            var worst = g.OrderBy(r => r.WinRate).First();
            if (best.WinRate - worst.WinRate >= 0.3f)
            {
                log.Add($"Deck '{g.Key}' is highly pilot-dependent: bot '{best.BotProfile}' achieved "
                      + $"{best.WinRate * 100:F0}% while bot '{worst.BotProfile}' only managed "
                      + $"{worst.WinRate * 100:F0}%. Recommend shipping '{g.Key}' with a suggested_ai "
                      + $"of '{best.BotProfile}'.");
            }
        }

        if (analysis.Notes is { Count: > 0 })
        {
            foreach (var n in analysis.Notes) log.Add("Observation: " + n);
        }

        log.Add("LEARNING HINT: When choosing a bot for a new deck, prefer profiles whose matched decks in this "
              + "tournament share archetype tags (aggro/control/midrange) with the new deck. Avoid pairing a "
              + "utility bot tuned for defence with an aggro deck — it will underweight tempo plays.");

        return log;
    }

    private static string InterpretMatchup(ResolvedPair a, ResolvedPair b, TournamentMatchupResultDto m)
    {
        // Characterise what the matchup says in plain prose. Kept short so
        // the learning log stays readable when printed as a document.
        if (m.Games == 0) return "No games completed.";
        bool sameDeck = string.Equals(a.DeckId, b.DeckId, StringComparison.Ordinal);
        bool sameBot = string.Equals(a.BotProfile, b.BotProfile, StringComparison.Ordinal);

        // Draw-dominated games carry no lesson about which pair is
        // stronger. Flag the stall explicitly instead of pretending AWinRate
        // reflects pilot skill (it doesn't when everyone's drawing).
        int decisive = m.AWins + m.BWins;
        if (decisive == 0)
        {
            if (sameDeck && sameBot)
                return "Self-mirror that stalled out on every game — no signal at all; the matchup's RNG + bot combination never closes out. Consider raising maxInputsPerGame or inspecting the encoding for stall loops.";
            return "Every game drew — no signal about which pair is stronger. Inspect the encoding for stall loops or raise maxInputsPerGame before drawing conclusions.";
        }

        // Derive skew from decisive games only so a 3-0-17 draw-heavy
        // matchup doesn't get mistakenly read as "B dominated".
        float decisiveWinRateA = (float)m.AWins / decisive;
        float skew = decisiveWinRateA - 0.5f;

        if (sameDeck && sameBot)
            return "Self-mirror — indicates raw seed-driven variance and whether the pairing is stable given the RNG.";
        if (sameDeck)
            return Math.Abs(skew) >= 0.2f
                ? $"Same deck, different bots: '{(skew > 0 ? a.BotProfile : b.BotProfile)}' pilots '{a.DeckId}' better than '{(skew > 0 ? b.BotProfile : a.BotProfile)}' in decisive games ({decisive} of {m.Games}). Treat as evidence of pilot skill on this deck."
                : "Same deck, different bots — bots are roughly comparable here, so pick by preference.";
        if (sameBot)
            return Math.Abs(skew) >= 0.2f
                ? $"Same bot, different decks: bot '{a.BotProfile}' prefers deck '{(skew > 0 ? a.DeckId : b.DeckId)}' over '{(skew > 0 ? b.DeckId : a.DeckId)}'. Evidence of deck affinity within this bot."
                : "Same bot, different decks — the bot plays both decks to similar effect, suggesting deck archetype parity.";
        return Math.Abs(skew) >= 0.2f
            ? $"Different deck + bot: the ({(skew > 0 ? a.BotProfile : b.BotProfile)}, {(skew > 0 ? a.DeckId : b.DeckId)}) pair wins — hard to attribute to either axis alone; follow up with a controlled same-bot or same-deck matchup to isolate."
            : "Different deck + bot, close outcome — either both pairs are balanced or the dominant factor cancels out. Follow up with a controlled matchup.";
    }

    private static string? WriteTournamentLog(
        TournamentConfigV2 cfg,
        IReadOnlyList<TournamentPairRowDto> rows,
        IReadOnlyList<TournamentMatchupResultDto> matchups,
        TournamentAnalysisDto analysis,
        IReadOnlyList<string> learningLog)
    {
        try
        {
            var repoRoot = FindRepoRoot();
            var dir = Path.Combine(repoRoot, "logs", "tournaments");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var slug = string.IsNullOrWhiteSpace(cfg.Name)
                ? "tournament"
                : new string((cfg.Name ?? "tournament").Select(c =>
                    char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray());
            var filename = $"{stamp}-{slug}.jsonl";
            var path = Path.Combine(dir, filename);

            using var writer = new StreamWriter(path);
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                @event = "tournament_begin",
                timestamp = DateTime.UtcNow.ToString("o"),
                name = cfg.Name,
                description = cfg.Description,
                pairs = cfg.Pairs,
                gamesPerMatchup = cfg.GamesPerMatchup,
                baseSeed = cfg.BaseSeed,
            }));
            foreach (var m in matchups)
            {
                writer.WriteLine(JsonSerializer.Serialize(new
                {
                    @event = "matchup_result",
                    aPairId = m.APairId,
                    bPairId = m.BPairId,
                    games = m.Games,
                    aWins = m.AWins,
                    bWins = m.BWins,
                    draws = m.Draws,
                    aWinRate = m.AWinRate,
                    avgSteps = m.AvgSteps,
                }));
            }
            foreach (var row in rows)
                writer.WriteLine(JsonSerializer.Serialize(new { @event = "pair_aggregate", row }));
            foreach (var lesson in learningLog)
                writer.WriteLine(JsonSerializer.Serialize(new { @event = "lesson", text = lesson }));
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                @event = "tournament_end",
                analysis,
            }));

            return Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
        }
        catch
        {
            // Disk writes are best-effort; the run result already made it
            // back over the wire. Don't fail the whole request because the
            // log couldn't be written.
            return null;
        }
    }

    // ─── GET /api/ai/tournament/logs ───────────────────────────────────

    private static IResult ListTournamentLogs()
    {
        var dir = Path.Combine(FindRepoRoot(), "logs", "tournaments");
        if (!Directory.Exists(dir))
            return Results.Ok(new TournamentLogListResponse(Array.Empty<TournamentLogSummaryDto>()));

        var logs = new List<TournamentLogSummaryDto>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.jsonl")
                                      .OrderByDescending(p => p, StringComparer.Ordinal))
        {
            var summary = SummariseLog(path);
            if (summary is not null) logs.Add(summary);
        }
        return Results.Ok(new TournamentLogListResponse(logs));
    }

    private static TournamentLogSummaryDto? SummariseLog(string path)
    {
        try
        {
            string? name = null;
            string? timestamp = null;
            int pairCount = 0;
            int totalGames = 0;
            string? topPerformer = null;
            float? topAdvantage = null;

            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("event", out var evProp)) continue;
                var evName = evProp.GetString();
                if (evName == "tournament_begin")
                {
                    if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                        name = n.GetString();
                    if (root.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.String)
                        timestamp = t.GetString();
                    if (root.TryGetProperty("pairs", out var p) && p.ValueKind == JsonValueKind.Array)
                        pairCount = p.GetArrayLength();
                }
                else if (evName == "matchup_result")
                {
                    if (root.TryGetProperty("games", out var g) && g.ValueKind == JsonValueKind.Number)
                        totalGames += g.GetInt32();
                }
                else if (evName == "tournament_end")
                {
                    if (root.TryGetProperty("analysis", out var a))
                    {
                        if (a.TryGetProperty("topPerformer", out var t) && t.ValueKind == JsonValueKind.String)
                            topPerformer = t.GetString();
                        if (a.TryGetProperty("topPerformerAdvantage", out var adv) && adv.ValueKind == JsonValueKind.Number)
                            topAdvantage = adv.GetSingle();
                    }
                }
            }

            var id = Path.GetFileNameWithoutExtension(path);
            var rel = Path.GetRelativePath(FindRepoRoot(), path).Replace('\\', '/');
            return new TournamentLogSummaryDto(
                Id: id,
                Path: rel,
                Timestamp: timestamp ?? "",
                Name: name,
                PairCount: pairCount,
                TotalGames: totalGames,
                TopPerformer: topPerformer,
                TopPerformerAdvantage: topAdvantage);
        }
        catch { return null; }
    }

    // ─── GET /api/ai/tournament/logs/{id} ──────────────────────────────

    private static IResult GetTournamentLog(string id)
    {
        // id is the filename sans extension; reject anything that tries to
        // step outside logs/tournaments/ via path separators or '..'.
        if (id.Contains("..") || id.Contains('/') || id.Contains('\\'))
            return Results.BadRequest(new { error = "Invalid log id." });

        var path = Path.Combine(FindRepoRoot(), "logs", "tournaments", id + ".jsonl");
        if (!File.Exists(path))
            return Results.NotFound(new { error = $"Log '{id}' not found." });

        // Stream as plain text so an LLM reading the log reads the JSONL
        // verbatim. A wrapping JSON envelope would double-encode it.
        return Results.File(File.ReadAllBytes(path), "application/x-ndjson");
    }

    private static TournamentRunner.BotBuilder? BuildBotFactory(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile)) return null;
        if (profile == "fixed") return () => new FixedLadderBot();
        if (profile == "utility") return () => UtilityBotFactory.Build(memory: new PhaseMemory());
        if (profile.StartsWith("experimental/", StringComparison.Ordinal))
        {
            var slug = profile.Substring("experimental/".Length);
            var path = Path.Combine(FindRepoRoot(), "encoding", "ai", "experimental", slug, "weights.json");
            if (!File.Exists(path)) return null;
            WeightTable weights;
            try { weights = WeightTable.FromJson(File.ReadAllText(path)); }
            catch (WeightTableFormatException) { return null; }
            return () => UtilityBotFactory.Build(weights: weights, memory: new PhaseMemory());
        }
        return null;
    }

    private static List<(string A, string B)> BuildMatchupList(
        TournamentConfigV2 cfg, IReadOnlyList<ResolvedPair> pairs)
    {
        var list = new List<(string, string)>();
        if (cfg.Matchups is { Count: > 0 })
        {
            foreach (var m in cfg.Matchups)
                list.Add((m.APairId, m.BPairId));
            return list;
        }

        // Round-robin: every pair vs every pair. Mirror (A vs A) included
        // when IncludeMirror is true — useful for measuring raw pair
        // strength, off by default because cross-pair is usually the
        // interesting signal.
        for (int i = 0; i < pairs.Count; i++)
        {
            for (int j = i; j < pairs.Count; j++)
            {
                if (i == j && !cfg.IncludeMirror) continue;
                list.Add((pairs[i].PairId, pairs[j].PairId));
            }
        }
        return list;
    }

    private static TournamentAnalysisDto BuildAnalysis(
        IReadOnlyList<TournamentPairRowDto> rows,
        IReadOnlyList<TournamentMatchupResultDto> matchups)
    {
        var notes = new List<string>();
        int totalGames = matchups.Sum(m => m.Games);
        float avgLength = matchups.Count == 0
            ? 0f
            : (float)matchups.Select(m => (double)m.AvgSteps * m.Games).Sum() / Math.Max(1, totalGames);

        string? top = rows.Count > 0 ? rows[0].PairId : null;
        string? bottom = rows.Count > 0 ? rows[^1].PairId : null;
        float? advantage = rows.Count >= 2 ? rows[0].WinRate - rows[1].WinRate : (float?)null;

        // Most-balanced matchup: |aWinRate - 0.5| closest to 0 with enough
        // games to be meaningful. Most-lopsided: |aWinRate - 0.5| furthest
        // from 0 (either direction).
        string? balanced = null, lopsided = null;
        string? longest = null, shortest = null;
        if (matchups.Count > 0)
        {
            var balSorted = matchups.OrderBy(m => Math.Abs(m.AWinRate - 0.5f)).ToArray();
            balanced = $"{balSorted[0].APairId} vs {balSorted[0].BPairId}";
            var lopSorted = matchups.OrderByDescending(m => Math.Abs(m.AWinRate - 0.5f)).ToArray();
            lopsided = $"{lopSorted[0].APairId} vs {lopSorted[0].BPairId}";

            var byLen = matchups.OrderByDescending(m => m.AvgSteps).ToArray();
            longest = $"{byLen[0].APairId} vs {byLen[0].BPairId} ({byLen[0].AvgSteps:F0} steps)";
            shortest = $"{byLen[^1].APairId} vs {byLen[^1].BPairId} ({byLen[^1].AvgSteps:F0} steps)";
        }

        if (top is not null && advantage is > 0.2f)
            notes.Add($"Clear winner: '{top}' leads runner-up by {advantage.Value * 100:F0} percentage points.");
        else if (top is not null && advantage is > 0f)
            notes.Add($"Close race: '{top}' edges out runner-up by {advantage.Value * 100:F0} points — consider more games per matchup.");

        var drawHeavy = matchups.Where(m => m.Games > 0 && (float)m.Draws / m.Games >= 0.4f).ToArray();
        if (drawHeavy.Length > 0)
            notes.Add($"{drawHeavy.Length} matchup(s) drew ≥ 40% of games — worth inspecting for stall states.");

        var stallers = matchups.Where(m => m.AvgSteps >= 0.9f * 2000).ToArray();
        if (stallers.Length > 0)
            notes.Add($"{stallers.Length} matchup(s) approached the per-game input cap — consider raising maxInputsPerGame.");

        if (top is not null && bottom is not null && !string.Equals(top, bottom, StringComparison.Ordinal))
        {
            var bottomRow = rows[^1];
            var topRow = rows[0];
            if (topRow.WinRate - bottomRow.WinRate >= 0.5f)
                notes.Add($"Wide spread: '{top}' at {topRow.WinRate * 100:F0}%, '{bottom}' at {bottomRow.WinRate * 100:F0}%.");
        }

        return new TournamentAnalysisDto(
            TotalMatchups: matchups.Count,
            TotalGames: totalGames,
            AvgGameLength: avgLength,
            TopPerformer: top,
            TopPerformerAdvantage: advantage,
            WeakestPerformer: bottom,
            MostBalancedMatchup: balanced,
            MostLopsidedMatchup: lopsided,
            LongestGameMatchup: longest,
            ShortestGameMatchup: shortest,
            Notes: notes);
    }

    // ─── Internal helpers for v2 tournament ────────────────────────────

    private sealed record ResolvedPair(
        string PairId,
        string DeckId,
        IReadOnlyList<string> Cards,
        string BotProfile,
        TournamentRunner.BotBuilder Builder);

    private sealed class AggregateMut
    {
        public string PairId { get; }
        public string DeckId { get; }
        public string BotProfile { get; }
        public int Games { get; private set; }
        public int Wins { get; private set; }
        public int Losses { get; private set; }
        public int Draws { get; private set; }
        public long TotalSteps { get; private set; }

        public AggregateMut(string pairId, string deckId, string botProfile)
        {
            PairId = pairId;
            DeckId = deckId;
            BotProfile = botProfile;
        }

        public void Record(int wins, int losses, int draws, long steps, int games)
        {
            Wins += wins;
            Losses += losses;
            Draws += draws;
            Games += games;
            TotalSteps += steps;
        }

        public TournamentPairRowDto ToDto()
        {
            float winRate = Games == 0 ? 0f : (float)Wins / Games;
            float avgSteps = Games == 0 ? 0f : (float)TotalSteps / Games;
            return new TournamentPairRowDto(
                PairId, DeckId, BotProfile,
                Games, Wins, Losses, Draws, winRate, avgSteps);
        }
    }
}

// ─── DTOs ──────────────────────────────────────────────────────────────

public sealed record BotProfileDto(string Id, string Name, string Description);

public sealed record AiWeightsDto(
    string Source,
    string? Path,
    IReadOnlyList<string> ConsiderationKeys,
    string Json,
    bool EditorEnabled);

public sealed record AiWeightsWriteRequest(string Json);

public sealed record AiPreviewAction(string? Kind, string? Label, Dictionary<string, string>? Metadata);

public sealed record AiPreviewScoreRequest(
    int CpuAether,
    IReadOnlyList<AiPreviewAction>? LegalActions,
    string? WeightsJson);

public sealed record AiPreviewRow(
    string Kind,
    string Label,
    float Score,
    IReadOnlyDictionary<string, float> Breakdown);

public sealed record AiTournamentRequest(
    string DeckId,
    int Games,
    int Seed,
    IReadOnlyList<string>? Bots,
    int MaxInputsPerGame,
    int MaxEventsPerGame);

public sealed record AiTournamentRow(
    string BotName,
    int Games,
    int Wins,
    int Losses,
    int Draws,
    float WinRate,
    float AvgSteps);

public sealed record AiTournamentResponse(
    string DeckId,
    IReadOnlyList<AiTournamentRow> Rows,
    string Timestamp);

// ─── Tournament V2 DTOs ────────────────────────────────────────────────
//
// Canonical exportable / importable tournament config. The GUI exports
// this shape verbatim as a *.tournament.json file; an LLM wiring a
// tournament run up via the REST host POSTs the same body to
// /api/ai/tournament/run.

public sealed record TournamentPairDto(
    string PairId,
    string DeckId,
    string BotProfile,
    string? Label);

public sealed record TournamentMatchupDto(
    string APairId,
    string BPairId);

// LogLevel controls how verbose the run's log is:
//   silent  — no learning log, no on-disk log.
//   summary — (default) analysis block only; no on-disk log.
//   llm     — full learning log (one natural-language statement per
//             matchup + cross-matchup patterns + tuning hints), and a
//             JSONL copy is written to logs/tournaments/ so an LLM can
//             re-read past runs via GET /api/ai/tournament/logs.
// Unknown values fall back to "summary".
public sealed record TournamentConfigV2(
    int Version,
    string? Name,
    string? Description,
    IReadOnlyList<TournamentPairDto> Pairs,
    IReadOnlyList<TournamentMatchupDto>? Matchups,
    bool IncludeMirror,
    int GamesPerMatchup,
    int BaseSeed,
    int MaxInputsPerGame,
    int MaxEventsPerGame,
    string? LogLevel = "summary");

public sealed record TournamentPairRowDto(
    string PairId,
    string DeckId,
    string BotProfile,
    int Games,
    int Wins,
    int Losses,
    int Draws,
    float WinRate,
    float AvgSteps);

public sealed record TournamentMatchupResultDto(
    string APairId,
    string BPairId,
    int Games,
    int AWins,
    int BWins,
    int Draws,
    float AWinRate,
    float AvgSteps);

public sealed record TournamentAnalysisDto(
    int TotalMatchups,
    int TotalGames,
    float AvgGameLength,
    string? TopPerformer,
    float? TopPerformerAdvantage,
    string? WeakestPerformer,
    string? MostBalancedMatchup,
    string? MostLopsidedMatchup,
    string? LongestGameMatchup,
    string? ShortestGameMatchup,
    IReadOnlyList<string> Notes);

// LogLevel: active log level for this run (echoes the request).
// LearningLog: one statement per matchup + cross-pair patterns, populated
//   only when log level is "llm"; designed for LLMs to read/learn from.
// LogPath: relative path to the JSONL file on disk, or null if
//   persistence wasn't requested.
public sealed record TournamentRunResponseV2(
    TournamentConfigV2 Config,
    IReadOnlyList<TournamentPairRowDto> Pairs,
    IReadOnlyList<TournamentMatchupResultDto> Matchups,
    TournamentAnalysisDto Analysis,
    string Timestamp,
    string LogLevel,
    IReadOnlyList<string> LearningLog,
    string? LogPath);

public sealed record TournamentLogSummaryDto(
    string Id,
    string Path,
    string Timestamp,
    string? Name,
    int PairCount,
    int TotalGames,
    string? TopPerformer,
    float? TopPerformerAdvantage);

public sealed record TournamentLogListResponse(
    IReadOnlyList<TournamentLogSummaryDto> Logs);

public sealed record TournamentValidateResponse(
    bool Ok,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
