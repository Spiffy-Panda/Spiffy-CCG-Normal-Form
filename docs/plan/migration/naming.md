# Migration — naming

Proposals, not yet applied. Each has a rationale and a scope estimate.
Apply when the referenced code is already being touched; don't do a
bulk rename pass.

## High value

### 1. Resolve `Ccgnf.Interpreter.Interpreter` collision

**Now:** namespace `Ccgnf.Interpreter` contains class `Interpreter`.
Every consumer aliases it (`using InterpreterRt = Ccgnf.Interpreter.Interpreter;`)
or fully qualifies.

**Options:**

- **A. Rename the class to `Runtime`.** Reads well:
  `new Runtime(logger).Run(file, options)`. Matches "Scheduler", "Evaluator"
  pattern (noun, not verb). Touches:
  `src/Ccgnf/Interpreter/Interpreter.cs`,
  `src/Ccgnf.Cli/Program.cs`,
  `src/Ccgnf.Rest/Endpoints/*.cs`,
  `tests/Ccgnf.Tests/InterpreterTests.cs`.
  ~5 files, no public API change for library consumers who go through
  the `Run` method.
- **B. Rename the namespace to `Ccgnf.Runtime`.** Touches ~14 files
  (every file under `src/Ccgnf/Interpreter/` and every `using` of that
  namespace). Clearer but larger blast radius.

Recommendation: **A** (rename class to `Runtime`). Do it during the next
interpreter-facing change.

### 2. `AstArgNamed` vs `AstArgBinding`

**Now:** two record types for `name: value` vs `name=value`. The
distinction is real (constructor arg vs pattern binding) but the names
`Named` and `Binding` don't telegraph it.

**Proposal:** rename to `AstArgKeyword` and `AstArgMatch`. "Keyword" is
the standard term for named-args-with-colon; "Match" signals pattern
context. Update `AstBuilder`, `Evaluator.BuildEventLiteral`, and
`Interpreter.TryMatchPattern`.

Scope: 4 files. Medium value — mostly benefits new readers.

## Medium value

### 3. `RtValue` naming suffixes

**Now:** all `Rt`-prefixed. Fine. But `RtNoOp` vs `RtNone` vs
`RtUnbound` vs `RtVoid` are four distinct sentinels and the distinctions
are non-obvious at call sites.

**Proposal:** Keep the types; add XML doc to each making the
distinction explicit in IntelliSense.

| Type | When it's produced | Truthy? |
|------|--------------------|---------|
| `RtNone` | Author wrote `None` in source | no |
| `RtUnbound` | Characteristic never set | no |
| `RtNoOp` | Author wrote `NoOp` | yes |
| `RtVoid` | Effect ran; no value | yes |

### 4. `Zone.Contents` naming

**Now:** `List<int>` called `Contents`. The int IS the entity id; the
name is fine. Leave.

### 5. `Scheduler` is not a scheduler

**Now:** `Scheduler` holds the RNG and input queue. It doesn't schedule
anything; the Interpreter owns the event loop.

**Proposal:** Rename to `RunContext` or `HostEnvironment`. Reflects its
actual role (bundle of external-to-the-interpreter state).

Scope: `src/Ccgnf/Interpreter/Scheduler.cs` plus ~6 references. Do it
while touching the interpreter.

### 6. `IHostInputQueue` → `IInputQueue`

**Now:** `IHostInputQueue`. The "Host" prefix is redundant once you're
inside the interpreter library — of course it's the host's channel.

**Proposal:** drop the prefix. `IInputQueue`, `QueuedInputs` stays.

Scope: 3 references.

## Low value / deferred

- `GameState` — name fine; could be `World` but "GameState" is the term
  in `grammar/GrammarSpec.md`. Keep.
- `Entity` — too generic, but matches the spec's primitive name. Keep.
- `CcgnfParser` — the `Ccgnf` prefix is redundant inside the `Ccgnf`
  assembly, but ANTLR's generated `CcgnfParser` lives alongside, and
  both are reachable. Name avoids ambiguity. Keep.

## Not proposed

- Renaming `encoding/` → `resonance/`. The directory is deliberately
  game-agnostic; Resonance is today's default but the engine is
  multi-game-capable.
- Renaming `.ccgnf` files. The extension is the format, not the game.
- Renaming `grammar/GrammarSpec.md`. It is the spec; the path is baked
  into multiple docs.
