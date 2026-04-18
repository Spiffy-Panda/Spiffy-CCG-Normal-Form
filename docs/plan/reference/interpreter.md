# Reference — Interpreter runtime

Location: `src/Ccgnf/Interpreter/`.

## Entry points

### `Interpreter`

`Interpreter.cs`. Orchestrates a run.

```csharp
public sealed class Interpreter {
    Interpreter(ILogger<Interpreter>? log = null, ILoggerFactory? loggerFactory = null);
    GameState Run(AstFile file, InterpreterOptions? options = null);
}

public sealed class InterpreterOptions {
    int Seed { get; set; }
    IHostInputQueue? Inputs { get; set; }
    int DefaultDeckSize { get; set; } = 30;
    int MaxEventDispatches { get; set; } = 10_000;
}
```

`Run` flow:
1. Build `Scheduler(seed, inputs)`.
2. Build `GameState` via `StateBuilder`.
3. Seed decks (N anonymous `Card` entities per Player's Arsenal).
4. Enqueue `Event.GameStart`.
5. Drain the event loop.

### `ProjectLoader`

`ProjectLoader.cs`. Thin chain: Preprocess → Parse → AstBuild → Validate.

```csharp
public sealed class ProjectLoader {
    ProjectLoader(ILoggerFactory? loggerFactory = null);
    ProjectLoadResult LoadFromFiles(IEnumerable<string> paths, string sourceName = "<project>");
    ProjectLoadResult LoadFromDirectory(string directory, string sourceName = "<project>");
    ProjectLoadResult LoadFromSources(IEnumerable<SourceFile> sources, string sourceName = "<project>");
}
```

## State

### `GameState` — `GameState.cs`

Mutable world state. Owns all entities by id; caches `Game`, `Players`,
`Arenas` for O(1) access.

Public members:

- `Dictionary<int, Entity> Entities`
- `Entity Game` (set after construction)
- `List<Entity> Players`, `List<Entity> Arenas`
- `Dictionary<string, Entity> NamedEntities` (e.g., `"Player1"`, `"ArenaLeft"`)
- `EventQueue PendingEvents`
- `long StepCount`, `bool GameOver`
- `Entity AllocateEntity(string kind, string displayName)` — reserves a fresh id.

### `Entity` — `Entity.cs`

Live entity: Game, Player, Arena, Conduit, Card, Token, PlayBinding.

- `int Id`, `string Kind`, `string DisplayName` — immutable.
- `Dictionary<string, RtValue> Characteristics` — durable named values.
- `Dictionary<string, int> Counters` — aether, debt, integrity, …
- `HashSet<string> Tags` — flags.
- `Dictionary<string, Zone> Zones` — Arsenal, Hand, Cache, ResonanceField, Void.
- `Dictionary<string, RtValue> Parameters` — bound from `for`-clause or `InstantiateEntity`.
- `List<AbilityInstance> Abilities`
- `int? OwnerId`

### `Zone` — `Zone.cs`

```csharp
enum ZoneOrder { Unordered, Sequential, FIFO, LIFO }

public sealed class Zone {
    string Name { get; }
    ZoneOrder Order { get; }
    int? Capacity { get; }
    List<int> Contents { get; }   // entity ids
    int Count { get; }
}
```

**Draw invariant:** `Draw` removes from `Contents[^1]` (end of list). For
Arsenals seeded top-to-bottom, this means the top card is the last
element. Do not re-index.

### `GameEvent` — `GameEvent.cs`

```csharp
public sealed class GameEvent {
    string TypeName { get; }
    IReadOnlyDictionary<string, RtValue> Fields { get; }
}
```

### `EventQueue` — `EventQueue.cs`

FIFO. `Enqueue`, `TryDequeue(out)`, `Count`, `Snapshot()`.

### `AbilityInstance` — `AbilityInstance.cs`

```csharp
enum AbilityKind { Static, Triggered, OnResolve, Replacement, Activated }

public sealed class AbilityInstance {
    AbilityKind Kind { get; }
    int OwnerId { get; }
    IReadOnlyDictionary<string, AstExpr> Named { get; }
    IReadOnlyList<AstExpr> Positional { get; }

    AstExpr? OnPattern { get; }     // Named["on"]
    AstExpr? Effect { get; }        // Named["effect"]
    AstExpr? Rule { get; }          // Named["rule"]
    int UsedThisTurn { get; set; }  // for once_per_turn
}
```

## Evaluator

### `Evaluator` — `Evaluator.cs`

AST walker. Produces `RtValue`s and side-effects the `GameState` via
builtin dispatch.

```csharp
public sealed class Evaluator {
    Evaluator(GameState state, Scheduler scheduler, ILogger<Evaluator>? log = null);
    GameState State { get; }
    Scheduler Scheduler { get; }
    ILogger<Evaluator> Log { get; }

    RtValue Eval(AstExpr expr, RtEnv env);
    RtValue LookupMember(RtValue target, string member);   // used by Builtins
}
```

Identifier-resolution priority (in `EvalIdent`):
1. Lexical scope (lambda params, `let`, `ForEach` bindings).
2. Keyword literals: `true`, `false`, `None`, `Unbound`, `NoOp`.
3. Named entities: `Player1`, `ArenaLeft`, `Game`.
4. Fallback: `RtSymbol(name)`. The Validator is responsible for catching
   typos before this path silently succeeds.

`RtEventLit` (a `RtValue` subtype) captures `Event.Foo(...)` expressions
before they're emitted; `EmitEvent` unwraps it into a `GameEvent`.

### Control-flow builtins get raw AST

`If`, `Switch`, `Cond`, `When` are AST-node kinds (`AstIfExpr`, etc.),
handled directly in `Eval`. `Sequence`, `ForEach`, `Repeat`, `Choice`
are function calls routed to `Builtins`, which consume the raw
`AstFunctionCall` so they can decide which sub-expressions to evaluate.

### `RtEnv` — `RtEnv.cs`

Chain of frames.
```csharp
public sealed class RtEnv {
    static RtEnv Empty;
    bool TryLookup(string name, out RtValue value);
    RtEnv Extend(string name, RtValue value);
    RtEnv Extend(IReadOnlyList<(string, RtValue)> pairs);
}
```

### `RtValue` family — `RtValue.cs`

See [api/library.md](../api/library.md#runtime-value-types-public-for-host-mappers).

## Builtins

`Builtins.cs`. `internal static`. Dispatch table; see
[builtins.md](builtins.md) for the full catalog.

## State construction

### `StateBuilder` — `StateBuilder.cs`

Two-pass AST → `GameState`:

1. Walk `AstEntityDecl`s. For each:
   - `for`-clause present → iterate source set, instantiate per element,
     name via `ComputeInstanceName(base, index)` (`Player` + `1` → `Player1`).
   - No `for`-clause, no index params → singleton.
   - Parameterized template (`Conduit[owner, arena]`) → skip; runtime
     instantiates via `InstantiateEntity`.
   - `lifetime: ephemeral` → skip.
2. Walk `AstEntityAugment`s. Parse `Triggered(...)` / `Static(...)` /
   etc. into `AbilityInstance` and attach to the target entity.

Field handling in `PopulateBody`:
- `kind` — informational; entity kind already set at allocation.
- `characteristics` — each sub-field evaluates to an `RtValue`.
- `counters` — integer values only; non-int defaults to 0.
- `zones` — delegates to `BuildZone` which parses `Zone(order: …, capacity: …)`.
- `abilities` — empty list; real abilities come via augmentations.
- Anything else — stored as a characteristic.

## Scheduler

### `Scheduler` — `Scheduler.cs`

```csharp
public sealed class Scheduler {
    Scheduler(int seed, IHostInputQueue inputs, ILogger<Scheduler>? log = null);
    Random Rng { get; }
    IHostInputQueue Inputs { get; }
    int Seed { get; }
    int NextInt(int maxExclusive);
    void ShuffleInPlace<T>(IList<T> list);   // Fisher–Yates, seeded
}
```

### `IHostInputQueue` — `IHostInputQueue.cs`

```csharp
public interface IHostInputQueue {
    RtValue Next(string prompt);
    bool IsEmpty { get; }
}

public sealed class QueuedInputs : IHostInputQueue {
    QueuedInputs(IEnumerable<RtValue> values);
    // throws InvalidOperationException when queue exhausted
}
```

## Event loop

Implemented in `Interpreter.RunEventLoop` + `DispatchEvent` +
`TryMatchPattern`. Summary:

```
while !GameOver and queue.TryDequeue(e):
    step++
    for each Triggered ability on Game (declaration order):
        if TryMatchPattern(ability.OnPattern, e, out bindings):
            Eval(ability.Effect, RtEnv.Empty.Extend(bindings))
    # RunSbaPass() is a no-op in v1
    if e.TypeName == "GameEnd": GameOver = true
```

### Pattern matching

- `Event.Foo` → matches if `e.TypeName == "Foo"`, no bindings.
- `Event.Foo(name=value, ...)` → matches on type AND each named field.
- Binding heuristic: `IsBindingIdent(expr)` returns true iff `expr` is
  an `AstIdent` whose first character is a lowercase letter. Otherwise
  the expression is evaluated and compared literally.

## Test coverage

- `tests/Ccgnf.Tests/InterpreterTests.cs` — setup invariants, Round-1
  Rise aether + draw, structural determinism.
- Full REST pipeline: `tests/Ccgnf.Rest.Tests/EndpointsTests.cs::Run_Encoding_ProducesState`.
