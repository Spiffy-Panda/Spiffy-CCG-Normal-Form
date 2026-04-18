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
        // per-declaration path + line by regex-scanning the raw content.
        var fileDeclarations = IndexFileDeclarations(rawByPath);

        var cardLocations = LocationsForKind(fileDeclarations, "Card");
        var entityLocations = LocationsForKind(fileDeclarations, "Entity");
        var tokenLocations = LocationsForKind(fileDeclarations, "Token");

        _log.LogInformation(
            "ProjectCatalog: loaded {FileCount} files, {MacroCount} macros, errors={HasErrors}.",
            rawByPath.Count, result.MacroNames.Count, result.HasErrors);

        return new ProjectSnapshot(
            File: result.File,
            RawContent: rawByPath,
            MacroNames: result.MacroNames,
            FileDeclarations: fileDeclarations,
            CardLocations: cardLocations,
            EntityLocations: entityLocations,
            TokenLocations: tokenLocations,
            LoadedAt: DateTimeOffset.UtcNow);
    }

    private static readonly Regex CardRegex =
        new(@"^\s*Card\s+(?<name>\w+)\s*\{", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex EntityRegex =
        new(@"^\s*Entity\s+(?<name>\w+)(?<params>\[[^\]]*\])?", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TokenRegex =
        new(@"^\s*Token\s+(?<name>\w+)", RegexOptions.Multiline | RegexOptions.Compiled);
    // Augment: target path (dotted / indexed) then `+=` or ` = `, but not `==`.
    // Require at least one `.` or `[` in the target so we don't catch bare
    // assignments like `x = 1` inside macro bodies.
    private static readonly Regex AugmentRegex = new(
        @"^(?<target>\w+(?:\.\w+|\[[^\]]*\])+)\s*(?:\+=|=(?!=))",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static IReadOnlyDictionary<string, IReadOnlyList<FileDeclaration>> IndexFileDeclarations(
        IDictionary<string, string> rawByPath)
    {
        var byFile = new SortedDictionary<string, IReadOnlyList<FileDeclaration>>(StringComparer.Ordinal);
        foreach (var (path, content) in rawByPath)
        {
            var list = new List<FileDeclaration>();
            ScanInto(list, content, CardRegex, m =>
                new FileDeclaration("Card", m.Groups["name"].Value, $"Card {m.Groups["name"].Value}",
                    LineFromOffset(content, m.Index)));
            ScanInto(list, content, EntityRegex, m =>
            {
                string name = m.Groups["name"].Value;
                string parms = m.Groups["params"].Success ? m.Groups["params"].Value : "";
                return new FileDeclaration("Entity", name, $"Entity {name}{parms}",
                    LineFromOffset(content, m.Index));
            });
            ScanInto(list, content, TokenRegex, m =>
                new FileDeclaration("Token", m.Groups["name"].Value, $"Token {m.Groups["name"].Value}",
                    LineFromOffset(content, m.Index)));
            ScanInto(list, content, AugmentRegex, m =>
            {
                string target = m.Groups["target"].Value;
                return new FileDeclaration("Augment", target, $"Augment {target}",
                    LineFromOffset(content, m.Index));
            });
            list.Sort((a, b) => a.Line.CompareTo(b.Line));
            byFile[path] = list;
        }
        return byFile;
    }

    private static void ScanInto(
        List<FileDeclaration> into,
        string content,
        Regex regex,
        Func<Match, FileDeclaration> select)
    {
        foreach (Match m in regex.Matches(content)) into.Add(select(m));
    }

    private static IReadOnlyDictionary<string, DeclarationLocation> LocationsForKind(
        IReadOnlyDictionary<string, IReadOnlyList<FileDeclaration>> byFile,
        string kind)
    {
        var result = new Dictionary<string, DeclarationLocation>(StringComparer.Ordinal);
        foreach (var (path, decls) in byFile)
        {
            foreach (var d in decls)
            {
                if (d.Kind != kind) continue;
                if (result.ContainsKey(d.Name)) continue;
                result[d.Name] = new DeclarationLocation(path, d.Line);
            }
        }
        return result;
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

public sealed record FileDeclaration(string Kind, string Name, string Label, int Line);

public sealed record ProjectSnapshot(
    AstFile? File,
    IReadOnlyDictionary<string, string> RawContent,
    IReadOnlyList<string> MacroNames,
    IReadOnlyDictionary<string, IReadOnlyList<FileDeclaration>> FileDeclarations,
    IReadOnlyDictionary<string, DeclarationLocation> CardLocations,
    IReadOnlyDictionary<string, DeclarationLocation> EntityLocations,
    IReadOnlyDictionary<string, DeclarationLocation> TokenLocations,
    DateTimeOffset LoadedAt)
{
    public static ProjectSnapshot Empty { get; } = new(
        File: null,
        RawContent: new Dictionary<string, string>(),
        MacroNames: Array.Empty<string>(),
        FileDeclarations: new Dictionary<string, IReadOnlyList<FileDeclaration>>(),
        CardLocations: new Dictionary<string, DeclarationLocation>(),
        EntityLocations: new Dictionary<string, DeclarationLocation>(),
        TokenLocations: new Dictionary<string, DeclarationLocation>(),
        LoadedAt: DateTimeOffset.UtcNow);
}
