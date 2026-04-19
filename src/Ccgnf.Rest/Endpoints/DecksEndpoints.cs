using Ccgnf.Rest.Serialization;
using Ccgnf.Rest.Services;

namespace Ccgnf.Rest.Endpoints;

/// <summary>
/// Deck construction endpoints. Currently ships a single endpoint —
/// a seeded-RNG mock card pool for the Decks page's "Draft" format.
/// Expand with <c>/api/decks</c> if/when server-side deck persistence lands.
/// </summary>
internal static class DecksEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/decks/mock-pool", MockPool);
        app.MapGet("/api/decks/presets", Presets);
    }

    private static IResult Presets(DeckCatalog catalog) =>
        Results.Ok(catalog.Get());

    private static readonly IReadOnlyDictionary<string, int> RarityWeights =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["C"] = 44,
            ["U"] = 32,
            ["R"] = 18,
            ["M"] = 6,
        };

    private static IResult MockPool(MockPoolRequest req, ProjectCatalog catalog)
    {
        string format = (req.Format ?? "draft").ToLowerInvariant();
        int size = req.Size > 0 ? req.Size : 40;

        var snapshot = catalog.Get();
        var cards = CardsEndpoints.CardsFrom(snapshot);
        if (cards.Count == 0)
        {
            return Results.BadRequest(new { error = "No cards loaded in the catalog." });
        }

        if (format == "constructed")
        {
            return Results.Ok(new MockPoolResponse(
                Format: format,
                Seed: req.Seed,
                Cards: cards.Select(c => c.Name).ToArray()));
        }

        if (format != "draft")
        {
            return Results.BadRequest(new { error = $"Unknown format '{format}'." });
        }

        if (size > cards.Count)
        {
            return Results.BadRequest(new
            {
                error = $"Requested size {size} exceeds pool of {cards.Count} cards.",
            });
        }

        var rng = new Random(req.Seed);
        var pool = SampleWeighted(cards, size, rng);

        return Results.Ok(new MockPoolResponse(
            Format: format,
            Seed: req.Seed,
            Cards: pool.Select(c => c.Name).ToArray()));
    }

    private static List<CardDto> SampleWeighted(IReadOnlyList<CardDto> cards, int size, Random rng)
    {
        // Weighted-without-replacement: each card's weight is its rarity's
        // target percentage (C 44 / U 32 / R 18 / M 6). Cards without a
        // known rarity get weight 1. Random.NextDouble picks by cumulative
        // weight; picked cards drop out of the pool.
        var remaining = new List<(CardDto card, double weight)>(cards.Count);
        foreach (var c in cards)
        {
            double w = RarityWeights.TryGetValue(c.Rarity, out var wt) ? wt : 1;
            remaining.Add((c, w));
        }

        var result = new List<CardDto>(size);
        for (int i = 0; i < size && remaining.Count > 0; i++)
        {
            double total = 0;
            foreach (var entry in remaining) total += entry.weight;
            double r = rng.NextDouble() * total;
            double cursor = 0;
            int pickIndex = remaining.Count - 1;
            for (int j = 0; j < remaining.Count; j++)
            {
                cursor += remaining[j].weight;
                if (cursor >= r) { pickIndex = j; break; }
            }
            result.Add(remaining[pickIndex].card);
            remaining.RemoveAt(pickIndex);
        }
        return result;
    }
}
