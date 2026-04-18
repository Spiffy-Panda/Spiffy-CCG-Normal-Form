using System.Text.RegularExpressions;
using Ccgnf.Ast;
using Ccgnf.Interpreter;
using Microsoft.Extensions.Logging;

namespace Ccgnf.Rest.Services;

/// <summary>
/// Cached view of the CCGNF project loaded from <c>CCGNF_PROJECT_ROOT</c>
/// (default <c>encoding/</c>) relative to the repository root. Resolves the
/// repo root by walking up from <see cref="AppContext.BaseDirectory"/> looking
/// for <c>Ccgnf.sln</c>; this is the same heuristic the REST integration
/// tests use.
///
/// Read-only endpoints (<c>/api/cards</c>, <c>/api/project</c>) depend on this
/// catalog via DI; per-request pipeline endpoints (<c>/api/run</c>,
/// <c>/api/sessions</c>) still construct a fresh <see cref="ProjectLoader"/>
/// against request-supplied files and never touch the catalog.
/// </summary>
public sealed class ProjectCatalog
{
    private readonly ILogger<ProjectCatalog> _log;
    private readonly ILoggerFactory _loggerFactory;
    private readonly object _lock = new();
    private ProjectSnapshot? _snapshot;

    public ProjectCatalog(ILogger<ProjectCatalog> log, ILoggerFactory loggerFactory)
    {
        _log = log;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Returns the current snapshot, loading it lazily on first access. Pass
    /// <paramref name="reload"/> to force a re-read from disk.
    /// </summary>
    public ProjectSnapshot Get(bool reload = false)
    {
        lock (_lock)
        {
            if (_snapshot is null || reload)
            {
                _snapshot = Load();
            }
            return _snapshot;
        }
    }

    private ProjectSnapshot Load()
    {
        string projectRootName = Environment.GetEnvironmentVariable("CCGNF_PROJECT_ROOT") ?? "encoding";
        string repoRoot = FindRepoRoot();
        string projectDir = Path.Combine(repoRoot, projectRootName);

        if (!Directory.Exists(projectDir))
        {
            _log.LogWarning("Project directory {Dir} does not exist; catalog is empty.", projectDir);
            return ProjectSnapshot.Empty;
        }

        var rawByPath = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var sources = new List<Ccgnf.Preprocessing.SourceFile>();

        foreach (var absolute in Directory.EnumerateFiles(projectDir, "*.ccgnf", SearchOption.AllDirectories)
                                          .OrderBy(p => p, StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(repoRoot, absolute).Replace('\\', '/');
            string content = File.ReadAllText(absolute);
            rawByPath[relative] = content;
            sources.Add(new Ccgnf.Preprocessing.SourceFile(relative, content));
        }

        var loader = new ProjectLoader(_loggerFactory);
        var result = loader.LoadFromSources(sources);

        // The preprocessor concatenates source files into one expanded string
        // and the parser tags tokens with its single sourceName ("<project>"),
        // so AST spans cannot carry the originating .ccgnf path. Recover
        // per-declaration file + line by regex-scanning the raw content.
        var cardLocations = IndexDeclarations(rawByPath, @"Card");
        var entityLocations = IndexDeclarations(rawByPath, @"Entity");
        var tokenLocations = IndexDeclarations(rawByPath, @"Token");

        _log.LogInformation(
            "ProjectCatalog: loaded {FileCount} files, {MacroCount} macros, errors={HasErrors}.",
            rawByPath.Count, result.MacroNames.Count, result.HasErrors);

        return new ProjectSnapshot(
            File: result.File,
            RawContent: rawByPath,
            MacroNames: result.MacroNames,
            CardLocations: cardLocations,
            EntityLocations: entityLocations,
            TokenLocations: tokenLocations,
            LoadedAt: DateTimeOffset.UtcNow);
    }

    private static IReadOnlyDictionary<string, DeclarationLocation> IndexDeclarations(
        IDictionary<string, string> rawByPath, string keyword)
    {
        var locations = new Dictionary<string, DeclarationLocation>(StringComparer.Ordinal);
        var regex = new Regex($@"^\s*{keyword}\s+(?<name>\w+)", RegexOptions.Multiline);

        foreach (var (path, content) in rawByPath)
        {
            foreach (Match match in regex.Matches(content))
            {
                string name = match.Groups["name"].Value;
                if (locations.ContainsKey(name)) continue;
                int line = LineFromOffset(content, match.Index);
                locations[name] = new DeclarationLocation(path, line);
            }
        }
        return locations;
    }

    private static int LineFromOffset(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n') line++;
        }
        return line;
    }

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

public sealed record DeclarationLocation(string Path, int Line);

public sealed record ProjectSnapshot(
    AstFile? File,
    IReadOnlyDictionary<string, string> RawContent,
    IReadOnlyList<string> MacroNames,
    IReadOnlyDictionary<string, DeclarationLocation> CardLocations,
    IReadOnlyDictionary<string, DeclarationLocation> EntityLocations,
    IReadOnlyDictionary<string, DeclarationLocation> TokenLocations,
    DateTimeOffset LoadedAt)
{
    public static ProjectSnapshot Empty { get; } = new(
        File: null,
        RawContent: new Dictionary<string, string>(),
        MacroNames: Array.Empty<string>(),
        CardLocations: new Dictionary<string, DeclarationLocation>(),
        EntityLocations: new Dictionary<string, DeclarationLocation>(),
        TokenLocations: new Dictionary<string, DeclarationLocation>(),
        LoadedAt: DateTimeOffset.UtcNow);
}
