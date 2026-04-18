# Reference — Preprocessor · Parser · AST · Validator · Diagnostics

## Diagnostics

Location: `src/Ccgnf/Diagnostics/`.

```csharp
enum DiagnosticSeverity { Info, Warning, Error }

sealed record Diagnostic(Severity, Code, Message, SourcePosition Position)
  // ToString() formats as "file:line:col: severity CODE: message"

readonly record struct SourcePosition(File, Line, Column)
  static Unknown = ("<unknown>", 0, 0)

readonly record struct SourceSpan(File, StartLine, StartColumn, EndLine, EndColumn)
  SourcePosition Start { get; }
  static Unknown
```

All stages emit `Diagnostic`s with unique code prefixes:
`PP###` preprocessor, `P###` parser (with the ANTLR listener assigning
`P001`..`P999`), `AST###` AST builder, `V###` validator.

## Preprocessor

Location: `src/Ccgnf/Preprocessor/`.

### `SourceFile(Path, Content)` — record

Filename + raw content. Preprocessor accepts one or many.

### `Preprocessor`

```csharp
public sealed class Preprocessor {
    Preprocessor(ILogger<Preprocessor>? log = null);
    PreprocessorResult Preprocess(SourceFile source);
    PreprocessorResult Preprocess(IEnumerable<SourceFile> sources);  // concatenates expanded output
}
```

Two-pass: extract `define` directives into a `MacroTable`, then expand
invocations via token substitution until a fixed point.

### `PreprocessorResult`

```csharp
class PreprocessorResult {
    string ExpandedText { get; }
    IReadOnlyList<Diagnostic> Diagnostics { get; }
    bool HasErrors { get; }
}
```

### Invariants

- **Macro bodies** terminate at EOF, a blank line at bracket depth 0, or
  another top-level starter (`define`/`Entity`/`Card`/`Token`) at line
  start and depth 0.
- **Cycle detection** via an expansion stack; recursive self-reference is
  PP011.
- **Label-position guard** — identifiers followed by `:` are field keys
  / named-arg labels and are **not** substituted. Keeps `Arsenal:`,
  `on:`, `effect:` intact through macro expansion.
- **Member-dot guard** — identifiers preceded by `.` are member names and
  are **not** macro-expanded. Prevents `Event.Destroy(target: u, reason: x)`
  from mis-matching the one-arg `Destroy(target)` macro.
- **SubstituteAndReposition** reanchors all tokens from a macro body to
  the invocation site's `SourcePosition`, so diagnostics point at the
  caller, not the `define`.

### Internal types (not public)

`PpToken` / `PpTokenKind`, `PpTokenizer`, `MacroTable`, `MacroDefinition`.

## Parser

Location: `src/Ccgnf/Parsing/`.

### `CcgnfParser`

```csharp
public sealed class CcgnfParser {
    CcgnfParser(ILogger<CcgnfParser>? log = null);
    ParseResult Parse(string preprocessedText, string sourceName = "<preprocessed>");
}
```

Facade over ANTLR-generated `CcgnfLexer` + `Grammar.CcgnfParser`. Strips
the default error listeners and installs `AntlrDiagnosticListener`.

### `ParseResult`

```csharp
class ParseResult {
    IParseTree? Tree { get; }
    IReadOnlyList<Diagnostic> Diagnostics { get; }
    bool HasErrors { get; }
}
```

### Grammar

Source of truth: `src/Ccgnf/Grammar/Ccgnf.g4`. Generated C# lives under
`obj/` and is `.gitignore`d. See `reference/ast-nodes.md` for the
AST shape the grammar ultimately produces (by way of `AstBuilder`).

## AST

Locations: `src/Ccgnf/Ast/AstNodes.cs`, `AstBuilder.cs`, `AstResult.cs`.

Record hierarchy documented separately in [ast-nodes.md](ast-nodes.md);
the entry point is:

### `AstBuilder`

```csharp
public sealed class AstBuilder {
    AstBuilder(ILogger<AstBuilder>? log = null);
    AstResult Build(IParseTree tree, string sourceName);
}
```

Converts ANTLR concrete tree to typed records. Emits `AST###` codes for
unrecognized shapes; does no semantic checks.

### `AstResult`

```csharp
class AstResult {
    AstFile? File { get; }
    IReadOnlyList<Diagnostic> Diagnostics { get; }
    bool HasErrors { get; }
}
```

## Validator

Location: `src/Ccgnf/Validation/`.

### `Validator`

```csharp
public sealed class Validator {
    Validator(ILogger<Validator>? log = null);
    ValidationResult Validate(AstFile file);
}
```

v1 rule coverage:

| Code | Rule |
|------|------|
| V100 | Duplicate top-level declaration name. |
| V101 | Duplicate Card/Token name. |
| V200 | Builtin call with wrong arity (see `BuiltinSignatures`). |
| V300 | R-5: Debt computation must use `printed_cost`, not `effective_cost`. |
| V400 | Forbidden `Procedure` identifier at top level (defense in depth; grammar already rejects). |

Out of v1 scope: R-1..R-4, R-6 enforcement; once-per-turn sanity;
type-flow analysis. Tracked in `grammar/GrammarSpec.md` §12.

### `ValidationResult`

```csharp
class ValidationResult {
    IReadOnlyList<Diagnostic> Diagnostics { get; }
    bool HasErrors { get; }
}
```

### `BuiltinSignatures` — internal

`IReadOnlyDictionary<string, Arity>` of known builtins. Source of truth
for V200. When adding a new builtin, update this table so arity errors
surface before the interpreter runs.

## Test coverage

| Target | Test file |
|--------|-----------|
| Preprocessor | `tests/Ccgnf.Tests/PreprocessorTests.cs` |
| Parser | `tests/Ccgnf.Tests/ParserTests.cs` |
| AstBuilder | `tests/Ccgnf.Tests/AstBuilderTests.cs` |
| Validator | `tests/Ccgnf.Tests/ValidatorTests.cs` |
| Whole-file corpus | `tests/Ccgnf.Tests/EncodingCorpusTests.cs` |
