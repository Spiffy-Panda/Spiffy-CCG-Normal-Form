# Architecture (UML-flat)

The system is a library (`Ccgnf`) plus three hosts (CLI, REST, Godot) that
compose it. Linux is the first-class build target; Windows is a convenience.

## Layering

```
┌───────────────────────────────────────────────────────────────┐
│ Hosts:  Ccgnf.Cli    Ccgnf.Rest           Ccgnf.Godot (todo)  │
│         └──┬────┬────────┬─────┬──────────────┬────┘          │
├──────────────┼────┼─────────┼───────┼──────────────┼──────────┤
│ Library (Ccgnf):    Diagnostics                               │
│                           │                                   │
│   Preprocessor → Parser → AST → Validator → Interpreter       │
│                                                  │            │
│                           GameState ◀─ StateBuilder           │
│                           Evaluator ◀─ Builtins               │
│                           Scheduler, EventQueue               │
└───────────────────────────────────────────────────────────────┘
```

## Pipeline (five stages)

Each stage is a separate class; each produces a Result object with
diagnostics. The next stage accepts the previous stage's output as input.

| Stage | Input | Output | Primary class | File |
|-------|-------|--------|---------------|------|
| Preprocess | `SourceFile` (or many) | `PreprocessorResult` (expanded text + diags) | `Preprocessor` | [src/Ccgnf/Preprocessor/Preprocessor.cs](../../src/Ccgnf/Preprocessor/Preprocessor.cs) |
| Parse | Expanded text | `ParseResult` (ANTLR parse tree + diags) | `CcgnfParser` | [src/Ccgnf/Parsing/CcgnfParser.cs](../../src/Ccgnf/Parsing/CcgnfParser.cs) |
| AST build | Parse tree | `AstResult` (`AstFile` + diags) | `AstBuilder` | [src/Ccgnf/Ast/AstBuilder.cs](../../src/Ccgnf/Ast/AstBuilder.cs) |
| Validate | `AstFile` | `ValidationResult` (diags only) | `Validator` | [src/Ccgnf/Validation/Validator.cs](../../src/Ccgnf/Validation/Validator.cs) |
| Interpret | `AstFile` + options | `GameState` | `Interpreter` | [src/Ccgnf/Interpreter/Interpreter.cs](../../src/Ccgnf/Interpreter/Interpreter.cs) |

The whole chain is wrapped by `ProjectLoader` for convenience. REST and
CLI both go through it.

## Runtime data model

```
GameState
 ├── Entities : Dictionary<int, Entity>         (all entities by id)
 ├── NamedEntities : Dictionary<string, Entity> (Player1, Game, ArenaLeft, …)
 ├── Game : Entity                              (singleton)
 ├── Players : List<Entity>
 ├── Arenas : List<Entity>
 ├── PendingEvents : EventQueue
 ├── StepCount : long
 └── GameOver : bool

Entity
 ├── Id, Kind, DisplayName            (identity)
 ├── Characteristics : Dict<str, RtValue>  (durable named values)
 ├── Counters        : Dict<str, int>      (aether, debt, integrity, …)
 ├── Tags            : HashSet<str>        (flags — "collapsed", …)
 ├── Zones           : Dict<str, Zone>     (Arsenal, Hand, Cache, …)
 ├── Parameters      : Dict<str, RtValue>  (bound from for-clause or InstantiateEntity)
 ├── Abilities       : List<AbilityInstance>
 └── OwnerId         : int?

Zone
 ├── Name
 ├── Order      : Unordered | Sequential | FIFO | LIFO
 ├── Capacity   : int?
 └── Contents   : List<int>                (entity ids)

AbilityInstance
 ├── Kind       : Static | Triggered | OnResolve | Replacement | Activated
 ├── OwnerId    : int
 ├── Named      : Dict<str, AstExpr>   (on: effect: rule: check_at: apply_at: …)
 └── Positional : List<AstExpr>

GameEvent
 ├── TypeName  : "GameStart" | "PhaseBegin" | "FirstPlayerChosen" | …
 └── Fields    : Dict<str, RtValue>

RtValue (sealed-record union)
 ├── RtInt, RtString, RtBool
 ├── RtSymbol                  enum-like (Rise, EMBER, Left)
 ├── RtSet, RtList, RtTuple
 ├── RtEntityRef, RtZoneRef
 ├── RtLambda                  AST + captured env
 ├── RtNone, RtUnbound         distinct sentinels
 ├── RtNoOp                    zero-effect value
 └── RtVoid                    result of an executed effect
```

## Event loop

Owned by `Interpreter`; runs until `PendingEvents` drains or `GameOver`.

```
  Enqueue Event.GameStart
  while !GameOver && queue.TryDequeue(e):
    step++
    DispatchEvent(e):
      for each Triggered ability on Game (declaration order):
        if pattern matches e:  bind variables; Eval(effect)   ← may enqueue more events
    RunSbaPass()                                              ← wired, inert in v1
```

v1 limitations to know:

- No `Replacement` interception (abilities that would modify `e` before commit).
- No `Activated` abilities (they need an input endpoint).
- No `OnResolve` handling distinct from Triggered.
- SBA pass is a no-op; the `check_at: continuously` Static abilities
  declared in `encoding/engine/07-sba.ccgnf` are parsed but not run.

## Pattern matching (trigger dispatch)

The `on:` named arg of a Triggered ability is either:

- `Event.Foo` — matches events of `TypeName == "Foo"`, no bindings.
- `Event.Foo(field=value, …)` — matches `TypeName == "Foo"` AND each
  listed field. For each field's value expression:
  - An identifier starting with a **lowercase letter** is a **fresh
    binding** — the event's field value binds to that name in the
    effect's environment.
  - Any other form is a **literal** — compared for equality via
    `Evaluator.RtEquals`.

Example: `on: Event.PhaseBegin(phase=Rise, player=p)` matches
PhaseBegin events with `phase == RtSymbol("Rise")` and binds `p` to the
event's `player` field.

## Determinism contract

Same `(AstFile, Seed, Inputs)` produces the same `GameState`. Two inputs
are threaded through a single `Scheduler`:

- `Random` — seeded with `Seed`, used by `Shuffle` and `RandomChoose`.
- `IHostInputQueue` — FIFO queue of pre-sequenced `RtValue`s consumed by
  `Choice` and (future) `Target`.

The structural-equality test in
[tests/Ccgnf.Tests/InterpreterTests.cs:136](../../tests/Ccgnf.Tests/InterpreterTests.cs)
enforces this.

## Host composition

Each host owns logger configuration + I/O; libraries accept `ILogger<T>`.

| Host | Entry | Logging | I/O surface |
|------|-------|---------|-------------|
| CLI | `src/Ccgnf.Cli/Program.cs` | `AddSimpleConsole` | stdin/stdout/stderr; exit code |
| REST | `src/Ccgnf.Rest/Program.cs` | ASP.NET Core default | HTTP on 19397 (`CCGNF_HTTP_PORT` to override) |
| Godot | (planned) | Custom `ILoggerProvider` → `GD.Print` etc. | in-process; no IPC |

## Cross-cutting: `ProjectLoader`

`src/Ccgnf/Interpreter/ProjectLoader.cs` is the single place that chains
Preprocess → Parse → AstBuild → Validate and returns an `AstFile`. Both
the CLI `--run` path and every REST pipeline endpoint go through it. If
you need a validated project in code, use this; don't reassemble the
chain by hand.

## Where things live (directory map)

```
src/
  Ccgnf/                      library — 5 subdirs, one per pipeline stage, plus Diagnostics + Interpreter
    Preprocessor/             Preprocessor, PpTokenizer, MacroTable, MacroDefinition, PpToken, SourceFile
    Parsing/                  CcgnfParser (facade), AntlrDiagnosticListener, ParseResult
    Grammar/                  Ccgnf.g4 (ANTLR; generated C# under obj/)
    Ast/                      AstNodes.cs (record hierarchy), AstBuilder, AstResult
    Validation/               Validator, BuiltinSignatures, ValidationResult
    Interpreter/              GameState, Entity, Zone, Evaluator, Builtins, StateBuilder,
                              Scheduler, EventQueue, Interpreter, ProjectLoader, StateSerializer,
                              RtValue, RtEnv, AbilityInstance, GameEvent, IHostInputQueue
    Diagnostics/              Diagnostic, SourcePosition, SourceSpan
  Ccgnf.Cli/                  CLI host
  Ccgnf.Rest/                 REST host: Endpoints/, Sessions/, Serialization/, wwwroot/
tests/
  Ccgnf.Tests/                unit + integration (fixtures under fixtures/)
  Ccgnf.Rest.Tests/           WebApplicationFactory integration
```

## Related reading

- [reference/](reference/) for per-module type catalogs.
- [api/rest.md](api/rest.md) for HTTP surface.
- [api/library.md](api/library.md) for C# consumer surface.
- `grammar/GrammarSpec.md` §8 for the interpreter spec this implementation targets.
