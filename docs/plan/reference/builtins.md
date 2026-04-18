# Reference — Builtins

Location: `src/Ccgnf/Interpreter/Builtins.cs`. `internal static class`
dispatched via `Builtins.TryDispatch(name, call, env, ev, out result)`.

Unknown names return `false` and are logged at Debug. Arity is enforced
earlier by the Validator (see `BuiltinSignatures`).

## Control flow

| Name | Shape | Semantics |
|------|-------|-----------|
| `Sequence` | `Sequence([e1, e2, …])` | Evaluate each AST element in order. Returns `RtVoid`. If the positional arg isn't an `AstListLit`, evaluates it once. |
| `ForEach` | `ForEach(x ∈ S, body)` or `ForEach((x,y) ∈ S₁ × S₂, body)` | Binds the variable(s) over elements of S and evaluates body. Lambda-predicate form (`ForEach(u -> pred, body)`) is a no-op in v1 — SBAs use this form and don't fire during Setup. |
| `Repeat` | `Repeat(n, body)` | Evaluate body n times. |
| `Choice` | `Choice(chooser: p, options: { k1: e1, k2: e2 })` | Pulls the next value from `Scheduler.Inputs`; matches against option keys; evaluates the winning branch. Missing key → `RtVoid`. |
| `NoOp` | `NoOp` | Returns `RtNoOp`. |
| `Guard` | `Guard(…)` | v1 no-op. |

## Value ops

| Name | Shape | Semantics |
|------|-------|-----------|
| `Count` | `Count(collectionOrZone)` | `Zone` → `Contents.Count`; list/set → `.Count`; else 0. |
| `Max` | `Max(a, b, …)` | Max over evaluated positional args as `int`. |
| `Min` | `Min(a, b, …)` | Min over evaluated positional args as `int`. |
| `other_player` | `other_player(p)` | Returns the `RtEntityRef` of the *other* Player. |
| `TurnOrderFrom` | `TurnOrderFrom(p)` | `RtList([p, other(p)])` for two-player v1. |

## Event ops

| Name | Shape | Semantics |
|------|-------|-----------|
| `EmitEvent` | `EmitEvent(Event.Foo(field=val, …))` | Evaluates its arg to an `RtEventLit`; unwraps to a `GameEvent` and enqueues. |

## Counter / characteristic ops

| Name | Shape | Semantics |
|------|-------|-----------|
| `SetCounter` | `SetCounter(entity, name, value)` | `entity.Counters[name] = int(value)`. `name` used as raw identifier text, NOT evaluated. |
| `IncCounter` | `IncCounter(entity, name, delta)` | `entity.Counters[name] += int(delta)` (treats missing as 0). |
| `ClearCounter` | `ClearCounter(entity, name)` | `entity.Counters[name] = 0`. |
| `SetCharacteristic` | `SetCharacteristic(entity, name, value)` | `entity.Characteristics[name] = value`. |
| `SetFlag` | `SetFlag(…)` | v1 no-op; Setup doesn't collapse conduits. |

**Identifier-as-name convention:** `SetCounter(p, debt, 0)` — `debt` is
an `AstIdent` used as a raw name, not evaluated. This is true for all
counter/flag ops.

## Deck ops

| Name | Shape | Semantics |
|------|-------|-----------|
| `Shuffle` | `Shuffle(p.Arsenal)` | Evaluates its arg to an `RtZoneRef`, `Scheduler.ShuffleInPlace` on `Contents`. |
| `Draw` | `Draw(p)` or `Draw(p, n)` or `Draw(p, amount: n)` | Moves up to `n` ids from the **end** of `p.Arsenal.Contents` to `p.Hand.Contents`. Gracefully truncates to Arsenal size; does NOT emit "deck-out" — that's handled inline in the Rise trigger. |

## Randomness / instantiation

| Name | Shape | Semantics |
|------|-------|-----------|
| `RandomChoose` | `RandomChoose(value: set, bind: Target.path)` | Picks one element via `Scheduler.Rng`; writes to `Target.path` if it resolves to a member access on a known entity. Returns the chosen element. |
| `InstantiateEntity` | `InstantiateEntity(kind: K, owner: o, …, initial_counters: {…})` | Allocates a fresh entity, copies named args into `Parameters`, writes `initial_counters` to `Counters`, sets `OwnerId` if `owner` is an `RtEntityRef`. Returns `RtEntityRef`. |

## Aether / resources

| Name | Shape | Semantics |
|------|-------|-----------|
| `RefillAether` | `RefillAether(p, amount: n)` | `p.Counters["aether"] = max(0, n)`. |
| `PayAether` | `PayAether(p, cost)` | `p.Counters["aether"] = max(0, current - cost)`. |

## Scaffolding stubs (inert in v1)

Parsed but not implemented; return `RtVoid` or empty collections. Exist so
the engine encoding files evaluate without runtime errors.

- `abilities_of_permanents(p)` → `RtList([])`
- `OpenTimingWindow(name, owner=…)` → `RtVoid`
- `DrainTriggersFor(window=…)` → `RtVoid`
- `BeginPhase(…)`, `EnterMainPhase(…)`, `ResolveClashPhase(…)` → `RtVoid`
- `Target(…)`, `PerformMulligan(…)`, `MoveTo(…)` → `RtVoid`
  - `Target`/`PerformMulligan` would be invoked only if a Mulligan
    `Choice` branch fires the `mulligan` key; the test fixture feeds
    `"pass"` for all four mulligan slots.

## Adding a builtin

1. Add a case to the `switch (name)` in `TryDispatch`.
2. Implement a `private static RtValue NewThing(AstFunctionCall call, RtEnv env, Evaluator ev)` method.
3. Add its arity to `BuiltinSignatures.Known` so V200 catches call-site mistakes.
4. Test it — unit tests for Builtins are sparse today; add one in
   `tests/Ccgnf.Tests/InterpreterTests.cs` if the path isn't covered by
   an existing integration test.
