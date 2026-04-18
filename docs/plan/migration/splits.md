# Migration — file splits

Files heading toward "too big to read cold" (threshold ~500 lines) and
their proposed cut lines. Apply opportunistically; don't bulk-split.

## `src/Ccgnf/Interpreter/Builtins.cs` — 553 lines

Currently one `internal static class Builtins` with a `TryDispatch`
switch and ~30 private static methods. Section comments group them into
clusters.

### Proposed split (partial class)

Keep the single class but split across files:

- `Builtins.cs` — the `TryDispatch` switch, shared helpers
  (`FirstPositional`, `ExprOf`, `IdentName`, `ParseInput`, `CountOf`),
  and any tiny stubs. ~120 lines.
- `Builtins.ControlFlow.cs` — `Sequence`, `ForEach`, `Repeat`, `Choice`.
- `Builtins.Values.cs` — `Count`/`CountOf`, `Max`, `Min`, `OtherPlayer`,
  `TurnOrderFrom`.
- `Builtins.Counters.cs` — `SetCounter`, `IncCounter`, `ClearCounter`,
  `SetCharacteristic`, `SetFlag`.
- `Builtins.Deck.cs` — `Shuffle`, `Draw`.
- `Builtins.Runtime.cs` — `EmitEvent`, `RandomChoose`, `InstantiateEntity`,
  `RefillAether`, `PayAether`.

All remain `partial class Builtins`. The dispatch switch stays in the
canonical file — one place to look for "is this name a builtin?".

**When to do it:** the next time a new builtin category is added.

## `src/Ccgnf/Ast/AstNodes.cs` — 248 lines, already below threshold

Not urgent. If/when the hierarchy crosses 400 lines, split along the
shape:

- `AstNodes.cs` — `AstNode` base + declarations.
- `AstExpressions.cs` — `AstExpr` subtree.
- `AstStructure.cs` — `AstBlock`, `AstField*`, `AstTargetPath`,
  `AstForClause`, `AstArg*`, `AstBraceEntry`.

## `src/Ccgnf/Ast/AstBuilder.cs` — 583 lines

**Don't split.** The file mirrors the grammar's rule tree 1:1; splitting
loses that co-location. Rely on section comments within the file.

## `src/Ccgnf/Preprocessor/Preprocessor.cs` — 527 lines

**Don't split** — two clean passes, `ExtractDefines` and `ExpandAll`,
plus helpers. Coherent as one file.

## `src/Ccgnf/Interpreter/Evaluator.cs` — 506 lines

**Don't split.** The expression switch is one long responsibility but
a single switch on `AstExpr` is easier to audit as one file than across
several.

## `src/Ccgnf/Interpreter/StateBuilder.cs` — 327 lines

Below threshold but dense. If it grows past 500, split along the two
passes:

- `StateBuilder.cs` — entity instantiation (pass 1).
- `StateBuilder.Augmentations.cs` — ability attachment (pass 2).

## `src/Ccgnf.Rest/Endpoints/PipelineEndpoints.cs` — 160 lines

Fine. If rooms endpoints grow, put them in a new file
`Endpoints/RoomEndpoints.cs` rather than adding to this one.

## `src/Ccgnf.Cli/Program.cs` — 217 lines

Procedural `Main` plus helpers. If `--run`'s flag handling balloons,
extract to `CommandLineOptions.cs`. Not urgent.

## Test files

| File | Lines | Action |
|------|-------|--------|
| `tests/Ccgnf.Tests/ParserTests.cs` | 259 | OK |
| `tests/Ccgnf.Tests/PreprocessorTests.cs` | 242 | OK |
| `tests/Ccgnf.Rest.Tests/EndpointsTests.cs` | 225 | OK |
| `tests/Ccgnf.Tests/AstBuilderTests.cs` | 192 | OK |

All below threshold; no action.

## Generated code

`src/Ccgnf/obj/*/CcgnfParser.cs` at ~3400 lines is ANTLR-generated and
gitignored. Not a split candidate.

## Apply checklist

When splitting:

1. Declare `partial class` on the new files.
2. Keep the namespace identical.
3. Each new file gets a header comment naming its category and pointing
   back to the canonical file.
4. Run `make ci` — no behavior change should be possible from a file
   split.
