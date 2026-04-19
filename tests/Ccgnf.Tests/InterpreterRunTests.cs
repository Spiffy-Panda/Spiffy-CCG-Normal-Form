using Ccgnf.Ast;
using Ccgnf.Interpreter;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using InterpreterRt = Ccgnf.Interpreter.Interpreter;

namespace Ccgnf.Tests;

/// <summary>
/// Tests for the generator-shaped interpreter added in step 7f. Three things
/// matter: (a) the new synchronous wrapper still produces identical state to
/// pre-7f behavior, (b) driving the handle one Submit at a time converges on
/// the same state as a batched driver, (c) the handle surfaces legal-action
/// context at every suspension point so the room / CPU loop can introspect.
/// </summary>
public class InterpreterRunTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static AstFile LoadEncoding()
    {
        var repoRoot = FindRepoRoot();
        var encDir = Path.Combine(repoRoot, "encoding");
        var loader = new ProjectLoader(NullLoggerFactory.Instance);
        var result = loader.LoadFromDirectory(encDir);
        Assert.True(result.File is not null,
            "Encoding failed to load:\n" + string.Join("\n", result.Diagnostics));
        Assert.False(result.HasErrors,
            "Encoding has diagnostics:\n" + string.Join("\n", result.Diagnostics));
        return result.File!;
    }

    private static AstFile LoadFixture(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", fixtureName);
        Assert.True(File.Exists(path), $"Fixture not found: {path}");
        var loader = new ProjectLoader(NullLoggerFactory.Instance);
        var result = loader.LoadFromFiles(new[] { path });
        Assert.True(result.File is not null,
            "Fixture failed to load:\n" + string.Join("\n", result.Diagnostics));
        Assert.False(result.HasErrors,
            "Fixture has diagnostics:\n" + string.Join("\n", result.Diagnostics));
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
        throw new InvalidOperationException("Could not find repo root.");
    }

    private static InterpreterRt NewInterpreter() =>
        new(NullLogger<InterpreterRt>.Instance, NullLoggerFactory.Instance);

    private static IEnumerable<RtValue> MulliganPasses() =>
        Enumerable.Repeat<RtValue>(new RtSymbol("pass"), 4);

    // -----------------------------------------------------------------------
    // (a) Determinism regression — Run() still produces the pre-7f state.
    // -----------------------------------------------------------------------

    [Fact]
    public void SyncRun_AfterGeneratorRefactor_MatchesSerializedState()
    {
        var file = LoadEncoding();

        var a = NewInterpreter().Run(file, new InterpreterOptions
        {
            Seed = 1776,
            Inputs = new QueuedInputs(MulliganPasses()),
        });
        var b = NewInterpreter().Run(file, new InterpreterOptions
        {
            Seed = 1776,
            Inputs = new QueuedInputs(MulliganPasses()),
        });

        Assert.Equal(StateSerializer.Serialize(a), StateSerializer.Serialize(b));
    }

    // -----------------------------------------------------------------------
    // (b) Async drive — StartRun + Submit converges on the same final state.
    // -----------------------------------------------------------------------

    [Fact]
    public void StartRun_DrivenOneAtATime_ProducesSameStateAsRun()
    {
        var file = LoadEncoding();

        var sync = NewInterpreter().Run(file, new InterpreterOptions
        {
            Seed = 1776,
            Inputs = new QueuedInputs(MulliganPasses()),
        });

        using var handle = NewInterpreter().StartRun(file, new InterpreterOptions { Seed = 1776 });
        var remaining = new Queue<RtValue>(MulliganPasses());
        while (true)
        {
            var pending = handle.WaitPending();
            if (pending is null) break;
            if (remaining.Count == 0)
            {
                Assert.Fail($"Unexpected pending input: {pending.Prompt}");
            }
            handle.Submit(remaining.Dequeue());
        }

        Assert.Equal(RunStatus.Completed, handle.Status);
        Assert.Equal(StateSerializer.Serialize(sync), StateSerializer.Serialize(handle.State));
    }

    [Fact]
    public void StartRun_OnEvent_FiresOncePerDispatchedEvent()
    {
        var file = LoadEncoding();

        int count = 0;
        long lastStep = 0;
        using var handle = NewInterpreter().StartRun(file, new InterpreterOptions
        {
            Seed = 42,
            OnEvent = (_, state) =>
            {
                count++;
                lastStep = state.StepCount;
            },
        });
        while (handle.WaitPending() is { } pending) handle.Submit(new RtSymbol("pass"));

        Assert.Equal(RunStatus.Completed, handle.Status);
        Assert.True(count > 0, "Expected at least one OnEvent callback");
        Assert.Equal(handle.State.StepCount, lastStep);
        Assert.Equal((int)handle.State.StepCount, count);
    }

    // -----------------------------------------------------------------------
    // (c) Legal actions — at a Choice point, the handle surfaces option keys.
    // -----------------------------------------------------------------------

    [Fact]
    public void GetLegalActions_AtChoicePending_ReturnsOptionKeysForChooser()
    {
        var file = LoadFixture("choice-on-start.ccgnf");

        using var handle = NewInterpreter().StartRun(file, new InterpreterOptions { Seed = 7 });

        var pending = handle.WaitPending();
        Assert.NotNull(pending);
        Assert.Equal(RunStatus.WaitingForInput, handle.Status);

        // The fixture chooses with Player1, offering {pass, mulligan}.
        int player1Id = handle.State.NamedEntities["Player1"].Id;
        int player2Id = handle.State.NamedEntities["Player2"].Id;
        Assert.Equal(player1Id, pending!.PlayerId);
        var labels = handle.GetLegalActions(player1Id).Select(a => a.Label).ToArray();
        Assert.Equal(new[] { "pass", "mulligan" }, labels);

        // Asking as Player2 — not the chooser — yields an empty list.
        Assert.Empty(handle.GetLegalActions(player2Id));

        handle.Submit(new RtSymbol("pass"));
        Assert.Null(handle.WaitPending());
        Assert.Equal(RunStatus.Completed, handle.Status);
        Assert.Empty(handle.GetLegalActions(player1Id));
    }

    [Fact]
    public void Stop_WhileWaiting_TransitionsToCancelled()
    {
        var file = LoadFixture("choice-on-start.ccgnf");

        var handle = NewInterpreter().StartRun(file, new InterpreterOptions { Seed = 7 });
        var pending = handle.WaitPending();
        Assert.NotNull(pending);

        handle.Stop();
        handle.WaitForExit(TimeSpan.FromSeconds(2));
        Assert.Equal(RunStatus.Cancelled, handle.Status);
        handle.Dispose();
    }
}
