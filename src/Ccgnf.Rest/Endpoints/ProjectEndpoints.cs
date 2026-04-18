using Ccgnf.Ast;
using Ccgnf.Rest.Serialization;
using Ccgnf.Rest.Services;

namespace Ccgnf.Rest.Endpoints;

/// <summary>
/// Read-only project-shape endpoints: files loaded, macros collected,
/// declaration index, and raw single-file content. All backed by the
/// <see cref="ProjectCatalog"/>.
/// </summary>
internal static class ProjectEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/project", GetProject);
        app.MapGet("/api/project/file", GetFile);
    }

    private static IResult GetProject(ProjectCatalog catalog, bool reload = false)
    {
        var snapshot = catalog.Get(reload);

        var files = snapshot.RawContent
            .Select(kvp => new ProjectFileDto(
                Path: kvp.Key,
                Bytes: System.Text.Encoding.UTF8.GetByteCount(kvp.Value)))
            .ToList();

        // Counts come from the AST (authoritative). byFile is produced from
        // the raw-content regex scan so each entry carries a real file path
        // and line — AST spans collapse to "<project>" after preprocessing.
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (snapshot.File is not null)
        {
            foreach (var decl in snapshot.File.Declarations)
            {
                string kind = KindOf(decl);
                counts[kind] = counts.TryGetValue(kind, out var n) ? n + 1 : 1;
            }
        }

        var byFile = new SortedDictionary<string, IReadOnlyList<ProjectDeclarationEntry>>(
            StringComparer.Ordinal);
        foreach (var (path, decls) in snapshot.FileDeclarations)
        {
            byFile[path] = decls
                .Select(d => new ProjectDeclarationEntry(d.Label, d.Line))
                .ToList();
        }

        return Results.Ok(new ProjectDto(
            Files: files,
            Macros: snapshot.MacroNames,
            Declarations: new ProjectDeclarationsDto(counts, byFile),
            LoadedAt: snapshot.LoadedAt.ToString("o")));
    }

    private static IResult GetFile(string path, ProjectCatalog catalog, bool reload = false)
    {
        if (!IsSafePath(path))
            return Results.BadRequest(new { error = "Invalid path." });

        var snapshot = catalog.Get(reload);
        if (!snapshot.RawContent.TryGetValue(path, out var content))
            return Results.NotFound();

        return Results.Text(content, "text/plain; charset=utf-8");
    }

    // -------------------------------------------------------------------------

    private static bool IsSafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.Contains("..", StringComparison.Ordinal)) return false;
        if (Path.IsPathRooted(path)) return false;
        if (path.Contains('\0')) return false;
        return true;
    }

    private static string KindOf(AstDeclaration decl) => decl switch
    {
        AstEntityDecl => "Entity",
        AstCardDecl => "Card",
        AstTokenDecl => "Token",
        AstEntityAugment => "Augment",
        _ => "Other",
    };
}
