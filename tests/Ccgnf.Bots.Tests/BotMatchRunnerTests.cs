using Ccgnf.Ast;
using Ccgnf.Bots.Bench;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ccgnf.Bots.Tests;

/// <summary>
/// End-to-end smoke tests for the bench harness. These exercise the full
/// interpreter → bot → submit loop against the real Resonance encoding.
/// Slow enough that we keep the game count small and rely on
/// <c>MaxEventDispatches</c> / <c>MaxInputsPerGame</c> to guarantee
/// termination.
/// </summary>
public class BotMatchRunnerTests
{
    private static AstFile LoadEncoding()
    {
        var repoRoot = FindRepoRoot();
        var encDir = Path.Combine(repoRoot, "encoding");
        var loader = new Ccgnf.Interpreter.ProjectLoader(NullLoggerFactory.Instance);
        var result = loader.LoadFromDirectory(encDir);
        Assert.True(result.File is not null, "Encoding failed to load");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        return result.File!;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ccgnf.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repo root");
    }

    [Fact]
    public void RunsOneMatchToTermination()
    {
        var file = LoadEncoding();

        var result = BotMatchRunner.RunMatch(
            file,
            deckA: null, // anonymous deck placeholders — interpreter seeds with DefaultDeckSize
            deckB: null,
            botA: new FixedLadderBot(),
            botB: new FixedLadderBot(),
            seed: 42,
            maxEvents: 50_000,
            maxInputs: 2_000);

        Assert.True(result.InputsServed > 0, "no inputs were served");
        // We don't require a clean winner — draws and step caps are valid
        // outcomes for this smoke test. What we *do* require is that the
        // runner actually drives the interpreter past the opening phase.
        Assert.True(result.StepCount > 0);
    }

    [Fact]
    public void TournamentCompletesSmallRun()
    {
        var file = LoadEncoding();

        var runner = new TournamentRunner();
        int progressCount = 0;
        runner.OnProgress += _ => progressCount++;

        var result = runner.Run(new TournamentRunner.TournamentConfig(
            File: file,
            DeckId: "anonymous",
            Cards: null,
            Entries: new[]
            {
                new TournamentRunner.Entry("fixed", () => new FixedLadderBot()),
            },
            GamesPerEntry: 2,
            BaseSeed: 1,
            MaxInputsPerGame: 2_000,
            MaxEventsPerGame: 50_000));

        Assert.Single(result.Rows);
        Assert.Equal("fixed", result.Rows[0].BotName);
        Assert.Equal(2, result.Rows[0].Games);
        Assert.True(progressCount >= 1, "tournament should emit progress per completed game");
    }
}
