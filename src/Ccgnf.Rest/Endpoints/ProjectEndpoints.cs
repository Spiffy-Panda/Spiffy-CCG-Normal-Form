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

        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var byFile = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

        if (snapshot.File is not null)
        {
            foreach (var decl in snapshot.File.Declarations)
            {
                string kind = KindOf(decl);
                counts[kind] = counts.TryGetValue(kind, out var n) ? n + 1 : 1;

                string path = PathFor(decl, snapshot);
                if (string.IsNullOrEmpty(path)) path = "<project>";
                string label = LabelOf(decl);
                if (!byFile.TryGetValue(path, out var list))
                {
                    list = new List<string>();
                    byFile[path] = list;
                }
                list.Add(label);
            }
        }

        var byFileReadOnly = byFile.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value,
            StringComparer.Ordinal);

        return Results.Ok(new ProjectDto(
            Files: files,
            Macros: snapshot.MacroNames,
            Declarations: new ProjectDeclarationsDto(counts, byFileReadOnly),
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

    private static string PathFor(AstDeclaration decl, ProjectSnapshot snapshot) => decl switch
    {
        AstCardDecl c when snapshot.CardLocations.TryGetValue(c.Name, out var cl) => cl.Path,
        AstEntityDecl e when snapshot.EntityLocations.TryGetValue(e.Name, out var el) => el.Path,
        AstTokenDecl t when snapshot.TokenLocations.TryGetValue(t.Name, out var tl) => tl.Path,
        _ => "",
    };

    private static string KindOf(AstDeclaration decl) => decl switch
    {
        AstEntityDecl => "Entity",
        AstCardDecl => "Card",
        AstTokenDecl => "Token",
        AstEntityAugment => "Augment",
        _ => "Other",
    };

    private static string LabelOf(AstDeclaration decl) => decl switch
    {
        AstEntityDecl e when e.IndexParams.Count > 0 =>
            $"Entity {e.Name}[{string.Join(",", e.IndexParams)}]",
        AstEntityDecl e => $"Entity {e.Name}",
        AstCardDecl c => $"Card {c.Name}",
        AstTokenDecl t => $"Token {t.Name}",
        AstEntityAugment a => $"Augment {a.Target.DisplayPath}",
        _ => decl.DisplayName,
    };
}
