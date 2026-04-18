namespace Ccgnf.Rest.Serialization;

// -----------------------------------------------------------------------------
// Card projection DTOs. Backs the Cards / Decks / Draft pages of the web app.
// `text` comes from the trailing "// text:" comment in the source; factions /
// type / cost / rarity / keywords come from walking the Card's block fields.
// -----------------------------------------------------------------------------

public sealed record CardDto(
    string Name,
    IReadOnlyList<string> Factions,
    string Type,
    int? Cost,
    string Rarity,
    IReadOnlyList<string> Keywords,
    string Text,
    string SourcePath,
    int SourceLine);

public sealed record DistributionRequest(IReadOnlyList<string>? Cards);

public sealed record DistributionDto(
    IReadOnlyDictionary<string, int> Faction,
    IReadOnlyDictionary<string, int> Type,
    IReadOnlyDictionary<string, int> Cost,
    IReadOnlyDictionary<string, int> Rarity);

public sealed record MockPoolRequest(string? Format, int Seed = 1234, int Size = 40);

public sealed record MockPoolResponse(string Format, int Seed, IReadOnlyList<string> Cards);
