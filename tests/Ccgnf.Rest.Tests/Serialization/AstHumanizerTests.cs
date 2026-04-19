using System.Text;
using Ccgnf.Ast;
using Ccgnf.Parsing;
using Ccgnf.Preprocessing;
using Ccgnf.Rest.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ccgnf.Rest.Tests.Serialization;

/// <summary>
/// Golden-snapshot tests for <see cref="AstHumanizer"/>. Each file under
/// <c>encoding/cards/</c> is humanized card-by-card and diffed against a
/// checked-in snapshot at <c>tests/Ccgnf.Rest.Tests/Snapshots/humanizer/</c>.
///
/// To regenerate snapshots intentionally, run the test suite with
/// <c>UPDATE_HUMANIZER_SNAPSHOTS=1</c> in the environment. The tests write
/// the new snapshot to disk and fail so the update is caught in review.
/// </summary>
public class AstHumanizerTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string SnapshotDir = Path.Combine(
        RepoRoot, "tests", "Ccgnf.Rest.Tests", "Snapshots", "humanizer");

    public static IEnumerable<object[]> CardFiles()
    {
        var cardsDir = Path.Combine(RepoRoot, "encoding", "cards");
        if (!Directory.Exists(cardsDir)) yield break;
        foreach (var path in Directory.GetFiles(cardsDir, "*.ccgnf").OrderBy(p => p))
            yield return new object[] { Path.GetFileName(path) };
    }

    [Theory]
    [MemberData(nameof(CardFiles))]
    public void CardFile_HumanizedOutput_MatchesSnapshot(string fileName)
    {
        var filePath = Path.Combine(RepoRoot, "encoding", "cards", fileName);
        var astFile = ParseCardFile(filePath);

        var sb = new StringBuilder();
        sb.Append("# Golden snapshot for ").Append(fileName).AppendLine();
        sb.AppendLine("# Regenerate with UPDATE_HUMANIZER_SNAPSHOTS=1.");
        sb.AppendLine();

        foreach (var decl in astFile.Declarations)
        {
            if (decl is not AstCardDecl card) continue;
            sb.Append("=== ").Append(card.Name).AppendLine(" ===");
            var abilitiesField = card.Body.Fields.FirstOrDefault(f => f.Key.Name == "abilities");
            var humanized = abilitiesField is null
                ? Array.Empty<string>()
                : AstHumanizer.HumanizeAbilitiesField(abilitiesField.Value);
            if (humanized.Count == 0) sb.AppendLine("(none)");
            else foreach (var line in humanized) sb.AppendLine(line);
            sb.AppendLine();
        }

        var actual = sb.ToString();
        var snapshotPath = Path.Combine(SnapshotDir, fileName + ".snap.txt");
        var update = Environment.GetEnvironmentVariable("UPDATE_HUMANIZER_SNAPSHOTS") == "1";

        if (!File.Exists(snapshotPath))
        {
            Directory.CreateDirectory(SnapshotDir);
            File.WriteAllText(snapshotPath, actual);
            Assert.Fail(
                $"Snapshot did not exist; wrote initial version to " +
                $"{Path.GetRelativePath(RepoRoot, snapshotPath)}. " +
                $"Review and commit, then re-run.");
        }

        var expected = File.ReadAllText(snapshotPath);
        if (update && expected != actual)
        {
            File.WriteAllText(snapshotPath, actual);
            Assert.Fail(
                $"Snapshot updated (UPDATE_HUMANIZER_SNAPSHOTS=1): " +
                $"{Path.GetRelativePath(RepoRoot, snapshotPath)}. " +
                $"Review and commit the new snapshot.");
        }

        Assert.Equal(NormalizeLineEndings(expected), NormalizeLineEndings(actual));
    }

    private static AstFile ParseCardFile(string absolutePath)
    {
        var text = File.ReadAllText(absolutePath);
        var relative = Path.GetRelativePath(RepoRoot, absolutePath).Replace('\\', '/');

        var pp = new Preprocessor(NullLogger<Preprocessor>.Instance)
            .Preprocess(new SourceFile(relative, text));
        Assert.False(pp.HasErrors, $"preprocess errors: {string.Join(", ", pp.Diagnostics)}");

        var parser = new CcgnfParser(NullLogger<CcgnfParser>.Instance);
        var parse = parser.Parse(pp.ExpandedText, sourceName: relative);
        Assert.False(parse.HasErrors, $"parse errors: {string.Join(", ", parse.Diagnostics.Take(5))}");

        var builder = new AstBuilder(NullLogger<AstBuilder>.Instance);
        var ast = builder.Build(parse.Tree!, sourceName: relative);
        Assert.False(ast.HasErrors, $"ast errors: {string.Join(", ", ast.Diagnostics.Take(5))}");
        Assert.NotNull(ast.File);
        return ast.File!;
    }

    private static string NormalizeLineEndings(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ccgnf.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root not found.");
    }
}
