using Ccgnf.Ast;
using Ccgnf.Rest.Serialization;
using Ccgnf.Rest.Services;

namespace Ccgnf.Rest.Endpoints;

/// <summary>
/// Read-only card projection endpoints. Drawn from the <see cref="ProjectCatalog"/>'s
/// cached <see cref="AstFile"/>; per-request interpreter runs still go through
/// <see cref="PipelineEndpoints"/>.
/// </summary>
internal static class CardsEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/cards", GetCards);
        app.MapPost("/api/cards/distribution", GetDistribution);
    }

    private static IResult GetCards(ProjectCatalog catalog, string? reload = null)
    {
        var snapshot = catalog.Get(IsTruthy(reload));
        var cards = CardsFrom(snapshot);
        return Results.Ok(cards);
    }

    private static IResult GetDistribution(
        DistributionRequest? req,
        ProjectCatalog catalog,
        string? reload = null)
    {
        var snapshot = catalog.Get(IsTruthy(reload));
        var all = CardsFrom(snapshot);

        IEnumerable<CardDto> filtered = all;
        if (req?.Cards is { Count: > 0 } names)
        {
            var allow = new HashSet<string>(names, StringComparer.Ordinal);
            filtered = all.Where(c => allow.Contains(c.Name));
        }

        var faction = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var type = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var cost = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var rarity = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var card in filtered)
        {
            foreach (var f in card.Factions) Bump(faction, f);
            if (!string.IsNullOrEmpty(card.Type)) Bump(type, card.Type);
            Bump(cost, CostBucket(card.Cost));
            if (!string.IsNullOrEmpty(card.Rarity)) Bump(rarity, card.Rarity);
        }

        return Results.Ok(new DistributionDto(faction, type, cost, rarity));
    }

    // -------------------------------------------------------------------------

    internal static IReadOnlyList<CardDto> CardsFrom(ProjectSnapshot snapshot)
    {
        if (snapshot.File is null) return Array.Empty<CardDto>();
        var list = new List<CardDto>();
        foreach (var decl in snapshot.File.Declarations)
        {
            if (decl is AstCardDecl card)
            {
                string path = "";
                int line = 0;
                string? raw = null;
                if (snapshot.CardLocations.TryGetValue(card.Name, out var loc))
                {
                    path = loc.Path;
                    line = loc.Line;
                    snapshot.RawContent.TryGetValue(loc.Path, out raw);
                }
                list.Add(CardMapper.ToDto(card, raw, path, line));
            }
        }
        return list;
    }

    private static void Bump(IDictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;

    private static string CostBucket(int? cost) => cost switch
    {
        null => "?",
        >= 6 => "6+",
        _ => cost.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static bool IsTruthy(string? v)
    {
        if (string.IsNullOrEmpty(v)) return false;
        return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
