using Ccgnf.Parsing;
using Ccgnf.Preprocessing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ccgnf.Tests;

/// <summary>
/// Corpus tests: every .ccgnf file under encoding/ must preprocess + parse
/// cleanly. These are integration tests — they locate the repo root relative
/// to the test binary and read the source tree directly. They fail loudly
/// when the grammar regresses against real content.
/// </summary>
public class EncodingCorpusTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    public static IEnumerable<object[]> EncodingFiles()
    {
        var encDir = Path.Combine(RepoRoot, "encoding");
        if (!Directory.Exists(encDir))
        {
            // Guard against running before the repo is laid out; return a single
            // sentinel so the [Theory] reports a clear failure rather than 0 tests.
            yield return new object[] { "<encoding-directory-missing>" };
            yield break;
        }
        foreach (var path in Directory.GetFiles(encDir, "*.ccgnf", SearchOption.AllDirectories).OrderBy(p => p))
        {
            yield return new object[] { Path.GetRelativePath(RepoRoot, path).Replace('\\', '/') };
        }
    }

    [Theory]
    [MemberData(nameof(EncodingFiles))]
    public void EncodingFile_PreprocessesAndParsesCleanly(string relativePath)
    {
        Assert.NotEqual("<encoding-directory-missing>", relativePath);

        var absolutePath = Path.Combine(RepoRoot, relativePath);
        var text = File.ReadAllText(absolutePath);

        var pp = new Preprocessor(NullLogger<Preprocessing.Preprocessor>.Instance)
            .Preprocess(new SourceFile(relativePath, text));
        if (pp.HasErrors)
        {
            Assert.Fail($"Preprocessor errors in {relativePath}:\n" +
                        string.Join("\n", pp.Diagnostics));
        }

        var parser = new CcgnfParser(NullLogger<CcgnfParser>.Instance);
        var parse = parser.Parse(pp.ExpandedText, sourceName: relativePath);
        if (parse.HasErrors)
        {
            Assert.Fail($"Parser errors in {relativePath}:\n" +
                        string.Join("\n", parse.Diagnostics.Take(10)) +
                        (parse.Diagnostics.Count > 10
                            ? $"\n ...({parse.Diagnostics.Count - 10} more)"
                            : ""));
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ccgnf.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate Ccgnf.sln walking up from {AppContext.BaseDirectory}. " +
            "EncodingCorpusTests need the repo to be on-disk at the standard layout.");
    }
}
