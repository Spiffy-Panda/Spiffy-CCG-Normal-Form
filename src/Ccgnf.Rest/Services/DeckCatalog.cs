using System.Text.Json;
using Ccgnf.Rest.Serialization;
using Microsoft.Extensions.Logging;

namespace Ccgnf.Rest.Services;

/// <summary>
/// Cached view of <c>encoding/decks/*.deck.json</c>. Preset decks are
/// authored alongside the catalog and ship with the repo; the file on
/// disk is the source of truth. Each entry is validated against the
/// card list in <see cref="ProjectCatalog"/> — any card name that
/// doesn't resolve is reported in the DTO's <c>UnknownCards</c> field
/// rather than silently dropped, so design drift is visible.
/// </summary>
public sealed class DeckCatalog
{
    private readonly ILogger<DeckCatalog> _log;
    private readonly ProjectCatalog _projects;
    private readonly object _lock = new();
    private IReadOnlyList<PresetDeckDto>? _snapshot;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public DeckCatalog(ILogger<DeckCatalog> log, ProjectCatalog projects)
    {
        _log = log;
        _projects = projects;
    }

    public IReadOnlyList<PresetDeckDto> Get(bool reload = false)
    {
        lock (_lock)
        {
            if (_snapshot is null || reload) _snapshot = Load();
            return _snapshot;
        }
    }

    private IReadOnlyList<PresetDeckDto> Load()
    {
        string projectRootName = Environment.GetEnvironmentVariable("CCGNF_PROJECT_ROOT") ?? "encoding";
        string repoRoot = FindRepoRoot();
        string deckDir = Path.Combine(repoRoot, projectRootName, "decks");

        if (!Directory.Exists(deckDir))
        {
            _log.LogInformation("DeckCatalog: no decks directory at {Dir}; returning empty.", deckDir);
            return Array.Empty<PresetDeckDto>();
        }

        var knownCards = new HashSet<string>(
            _projects.Get().CardLocations.Keys, StringComparer.Ordinal);

        var decks = new List<PresetDeckDto>();
        foreach (var path in Directory.EnumerateFiles(deckDir, "*.deck.json", SearchOption.TopDirectoryOnly)
                                      .OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                var raw = File.ReadAllText(path);
                var file = JsonSerializer.Deserialize<DeckFile>(raw, JsonOpts);
                if (file is null)
                {
                    _log.LogWarning("DeckCatalog: {Path} deserialized to null.", path);
                    continue;
                }

                var entries = (file.Cards ?? Array.Empty<DeckCardFile>())
                    .Select(c => new DeckCardEntry(c.Name ?? "", c.Count))
                    .Where(e => !string.IsNullOrEmpty(e.Name))
                    .ToArray();

                var unknown = entries
                    .Where(e => !knownCards.Contains(e.Name))
                    .Select(e => e.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                int total = entries.Sum(e => e.Count);
                string id = file.Id ?? Path.GetFileNameWithoutExtension(path).Replace(".deck", "");

                decks.Add(new PresetDeckDto(
                    Id: id,
                    Name: file.Name ?? id,
                    Format: (file.Format ?? "constructed").ToLowerInvariant(),
                    Factions: file.Factions ?? Array.Empty<string>(),
                    Description: file.Description ?? "",
                    Cards: entries,
                    CardCount: total,
                    UnknownCards: unknown));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "DeckCatalog: failed to load {Path}.", path);
            }
        }

        _log.LogInformation("DeckCatalog: loaded {Count} preset decks from {Dir}.", decks.Count, deckDir);
        return decks;
    }

    private sealed record DeckFile(
        string? Id,
        string? Name,
        string? Format,
        IReadOnlyList<string>? Factions,
        string? Description,
        IReadOnlyList<DeckCardFile>? Cards);

    private sealed record DeckCardFile(string? Name, int Count);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ccgnf.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate Ccgnf.sln walking up from AppContext.BaseDirectory.");
    }
}
