# Audit — snapshot

Captured 2026-04-18. Refresh when it drifts from reality.

## Strengths

- **Stage boundaries are clean.** Preprocessor → Parser → AstBuilder →
  Validator → Interpreter; each has its own directory, test file, and
  Result type. No cross-stage leakage.
- **Host / library split is real.** Every library project depends only
  on `Microsoft.Extensions.Logging.Abstractions`. CLI adds Console,
  REST adds ASP.NET Core. Godot's path is reserved.
- **Determinism is testable, not aspirational.** `StateSerializer` +
  `InterpreterTests.SameSeedSameInputs_ProducesIdenticalState` catches
  drift.
- **Test coverage is broad where it matters.** Preprocessor, parser, AST,
  validator, corpus of all real `.ccgnf` files, interpreter integration,
  REST integration. 130 tests.
- **ProjectLoader is the single pipeline orchestrator.** Both CLI and
  REST consume it; no duplicate stage-chaining logic.

## Known gaps

### Scope-bound (expected, tracked)

- **Interpreter v1 only covers Setup → first Round-1 Rise.** Later
  phases, Interrupts/Debt, Phantom, Clash damage, and SBA execution are
  out of scope for this commit arc. See `grammar/GrammarSpec.md` §8 and
  `README.md` Status table.
- **REST sessions are read-only post-creation.** No `actions` endpoint
  until the interpreter supports mid-run input.
- **Godot host not scaffolded.** Design complete in spec §11.3.

### Coverage gaps

| Area | Status |
|------|--------|
| `Builtins.Draw` / `Shuffle` edge cases (empty deck, overflow) | Integration-tested only. No unit tests. |
| `StateBuilder` with malformed entity bodies (missing `kind`, bad `zones:` shape) | Not exercised. |
| `Evaluator.EvalIndex` on non-list targets | Logs Trace and returns `RtVoid`; no test. |
| `RandomChoose` with empty source set | Returns `RtVoid`; no test. |
| `EventQueue.Snapshot` stability under mutation | No test. |

### Naming / structural

- **`Ccgnf.Interpreter.Interpreter` collision.** Namespace + class share
  a name; consumers need `using InterpreterRt = Ccgnf.Interpreter.Interpreter;`.
  Candidates for rename in [../migration/naming.md](../migration/naming.md).
- **`Builtins.cs` is 553 lines.** Still single-responsibility (dispatch),
  but close to the "too big to read cold" threshold. Candidate split in
  [../migration/splits.md](../migration/splits.md).
- **`Evaluator.cs` at 506 lines** is the evaluator's complete expression
  grammar; splitting risks coherence. Leave alone for now.
- **`AstBuilder.cs` at 583 lines** mirrors the grammar's rules 1:1 and is
  easier to audit as one file. Leave alone.

### Documentation drift risks

- `grammar/GrammarSpec.md` §11 project layout is slightly stale (lists
  `Ccgnf.Grammar/` as a separate project; actual layout keeps the grammar
  inside `Ccgnf/`). Low-priority cleanup.
- `encoding/DESIGN-NOTES.md` references rulings R-1..R-6 by number but
  doesn't index them. Out of scope for this audit.

## Performance posture

Not a current concern. The whole pipeline over the real encoding runs in
under a second; the event loop halts at Round-1 Rise after < 10 events.
When the engine grows: `GameState.Entities` is a `Dictionary<int, Entity>`
with no pool; ability dispatch is O(abilities × events); event queue is
a plain `Queue<GameEvent>`. All fine until they aren't.

## Security posture

- REST has **no authentication** and binds only to `localhost` by default.
  Suitable for developer use. Rooms protocol introduces non-cryptographic
  join tokens; not safe to expose publicly as-is.
- CLI reads local files; no sandboxing.
- Preprocessor has a hard `MaxExpansionDepth = 64` guard against runaway
  macro recursion.
- `Interpreter` has `MaxEventDispatches = 10_000` safety cap.

## Open questions (parked)

- Will the frontend eventually need a real TypeScript typing for DTOs?
  For now hand-written interfaces in the Vite app are fine; a codegen
  from `Dtos.cs` is a nice-to-have if drift bites.
- When rooms land, who owns per-room `Scheduler` state? Currently
  `Scheduler` is constructed per `Interpreter.Run`; for long-lived rooms
  we'll need to keep a scheduler around to keep the RNG sequence stable.

## What this audit is not

- Not a to-do list for "clean up everything before the web app". The
  web arc is the priority. Anything here that doesn't block a step in
  [../steps/](../steps/) stays parked.
- Not a sign-off on grammar coverage. `grammar/GrammarSpec.md` §12 has
  the real open-questions list.
