# C# library API

Consumer-facing surface of the `Ccgnf` library (namespace roots:
`Ccgnf.Preprocessing`, `Ccgnf.Parsing`, `Ccgnf.Ast`, `Ccgnf.Validation`,
`Ccgnf.Interpreter`, `Ccgnf.Diagnostics`). Types below are the entry
points a host composes; see [../reference/](../reference/) for the full
type catalog.

## Common pattern

```csharp
// One-liner (what you probably want):
var loader = new ProjectLoader(loggerFactory);
var result = loader.LoadFromDirectory("encoding");
if (result.HasErrors) { /* print result.Diagnostics */ return; }

var interpreter = new Interpreter(loggerFactory.CreateLogger<Interpreter>(), loggerFactory);
var state = interpreter.Run(result.File!, new InterpreterOptions
{
    Seed = 42,
    Inputs = new QueuedInputs(new RtValue[] { new RtSymbol("pass"), ... }),
});
```

## Pipeline stages (use directly if you need per-stage control)

### `Ccgnf.Preprocessing.Preprocessor`

```csharp
public sealed class Preprocessor {
    public Preprocessor(ILogger<Preprocessor>? log = null);
    public PreprocessorResult Preprocess(SourceFile source);
    public PreprocessorResult Preprocess(IEnumerable<SourceFile> sources);
}
```

### `Ccgnf.Parsing.CcgnfParser`

```csharp
public sealed class CcgnfParser {
    public CcgnfParser(ILogger<CcgnfParser>? log = null);
    public ParseResult Parse(string preprocessedText, string sourceName = "<preprocessed>");
}
```

### `Ccgnf.Ast.AstBuilder`

```csharp
public sealed class AstBuilder {
    public AstBuilder(ILogger<AstBuilder>? log = null);
    public AstResult Build(Antlr4.Runtime.Tree.IParseTree tree, string sourceName);
}
```

### `Ccgnf.Validation.Validator`

```csharp
public sealed class Validator {
    public Validator(ILogger<Validator>? log = null);
    public ValidationResult Validate(AstFile file);
}
```

## Orchestrator

### `Ccgnf.Interpreter.ProjectLoader`

Chains all four stages above and returns a validated `AstFile`.

```csharp
public sealed class ProjectLoader {
    public ProjectLoader(ILoggerFactory? loggerFactory = null);
    public ProjectLoadResult LoadFromFiles(IEnumerable<string> paths, string sourceName = "<project>");
    public ProjectLoadResult LoadFromDirectory(string directory, string sourceName = "<project>");
    public ProjectLoadResult LoadFromSources(IEnumerable<SourceFile> sources, string sourceName = "<project>");
}

public sealed class ProjectLoadResult {
    public AstFile? File { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public bool HasErrors { get; }
}
```

## Interpreter

### `Ccgnf.Interpreter.Interpreter`

```csharp
public sealed class Interpreter {
    public Interpreter(ILogger<Interpreter>? log = null, ILoggerFactory? loggerFactory = null);
    public GameState Run(AstFile file, InterpreterOptions? options = null);
}

public sealed class InterpreterOptions {
    public int Seed { get; set; }
    public IHostInputQueue? Inputs { get; set; }
    public int DefaultDeckSize { get; set; } = 30;
    public int MaxEventDispatches { get; set; } = 10_000;
}
```

### `Ccgnf.Interpreter.IHostInputQueue`

```csharp
public interface IHostInputQueue {
    RtValue Next(string prompt);
    bool IsEmpty { get; }
}

// Default implementation:
public sealed class QueuedInputs : IHostInputQueue {
    public QueuedInputs(IEnumerable<RtValue> values);
}
```

### `Ccgnf.Interpreter.StateSerializer`

```csharp
public static class StateSerializer {
    public static string Serialize(GameState state);
}
```

Returns a canonical text dump used for determinism testing
([tests/Ccgnf.Tests/InterpreterTests.cs:136](../../tests/Ccgnf.Tests/InterpreterTests.cs)).

## Runtime value types (public for host mappers)

```csharp
public abstract record RtValue;
public sealed record RtInt(int V) : RtValue;
public sealed record RtString(string V) : RtValue;
public sealed record RtBool(bool V) : RtValue;
public sealed record RtSymbol(string Name) : RtValue;
public sealed record RtSet(IReadOnlyList<RtValue> Elements) : RtValue;
public sealed record RtList(IReadOnlyList<RtValue> Elements) : RtValue;
public sealed record RtTuple(IReadOnlyList<RtValue> Elements) : RtValue;
public sealed record RtEntityRef(int Id) : RtValue;
public sealed record RtZoneRef(int OwnerId, string Name) : RtValue;
public sealed record RtLambda(IReadOnlyList<string> Parameters, AstExpr Body) : RtValue;
public sealed record RtNone : RtValue;
public sealed record RtUnbound : RtValue;
public sealed record RtNoOp : RtValue;
public sealed record RtVoid : RtValue;
```

## State types

```csharp
public sealed class GameState {
    public Dictionary<int, Entity> Entities { get; }
    public Entity Game { get; set; }
    public List<Entity> Players { get; }
    public List<Entity> Arenas { get; }
    public Dictionary<string, Entity> NamedEntities { get; }
    public EventQueue PendingEvents { get; }
    public long StepCount { get; set; }
    public bool GameOver { get; set; }
    public Entity AllocateEntity(string kind, string displayName);
}

public sealed class Entity {
    public int Id { get; }
    public string Kind { get; }
    public string DisplayName { get; }
    public Dictionary<string, RtValue> Characteristics { get; }
    public Dictionary<string, int> Counters { get; }
    public HashSet<string> Tags { get; }
    public Dictionary<string, Zone> Zones { get; }
    public Dictionary<string, RtValue> Parameters { get; }
    public List<AbilityInstance> Abilities { get; }
    public int? OwnerId { get; set; }
}

public sealed class Zone {
    public string Name { get; }
    public ZoneOrder Order { get; }
    public int? Capacity { get; }
    public List<int> Contents { get; }
}
```

## Diagnostics

```csharp
public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    SourcePosition Position);

public readonly record struct SourcePosition(string File, int Line, int Column);
public readonly record struct SourceSpan(string File, int StartLine, int StartColumn, int EndLine, int EndColumn);
```

## What's NOT public

- Every type named `*Internal`, `PpToken*`, `MacroDefinition`, `MacroTable`
  is `internal`. Do not depend on them from outside the `Ccgnf` assembly.
- ANTLR-generated types under `Ccgnf.Grammar` are reachable but unstable —
  regeneration may rename. Go through `CcgnfParser` instead.
- `Builtins` is `internal static`. Add a builtin by editing the dispatch
  switch in `src/Ccgnf/Interpreter/Builtins.cs`.
