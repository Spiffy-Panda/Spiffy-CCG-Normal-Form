using System.Text.Json;
using Ccgnf.Bots;
using Ccgnf.Bots.Bench;
using Ccgnf.Bots.Utility;
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
    }

    private static bool EditorOn =>
        string.Equals(Environment.GetEnvironmentVariable("CCGNF_AI_EDITOR"), "1", StringComparison.Ordinal);

    // ─── GET /api/ai/bots ──────────────────────────────────────────────

    private static IResult GetBots()
    {
        var bots = new[]
        {
            new BotProfileDto("fixed", "Fixed Ladder", "Baseline ladder policy."),
            new BotProfileDto("utility", "Utility (default)", "Utility bot with default weights + phase BT."),
        };
        return Results.Ok(bots);
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
