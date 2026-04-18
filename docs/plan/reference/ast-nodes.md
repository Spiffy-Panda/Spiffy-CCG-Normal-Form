# Reference — AST node hierarchy

Location: `src/Ccgnf/Ast/AstNodes.cs`. All nodes are `sealed record`s
with a `SourceSpan Span` and structural equality.

## Top-level

```
AstNode (abstract)
└── AstFile (Span, Declarations: IReadOnlyList<AstDeclaration>)
```

## Declarations

```
AstDeclaration (abstract; DisplayName property)
├── AstEntityDecl    (Span, Name, IndexParams: List<string>, ForClause?, Body: AstBlock)
├── AstCardDecl      (Span, Name, Body)
├── AstTokenDecl     (Span, Name, Body)
└── AstEntityAugment (Span, Target: AstTargetPath, Value: AstExpr)
```

- `AstEntityDecl.IndexParams` — the `[p, q]` list in `Entity Foo[p, q] { … }`.
- `AstEntityDecl.ForClause` — `for p ∈ S` (below).
- `AstEntityAugment` — `Game.abilities += Triggered(…)`. `Target` is the
  dotted+indexed LHS; `Value` is the RHS expression.

## Structure inside declarations

```
AstForClause(Span, Variable: string, Source: AstExpr)
AstBlock(Span, Fields: IReadOnlyList<AstField>)
AstField(Span, Key: AstFieldKey, Value: AstFieldValue)
AstFieldKey(Span, Name: string, Indices: IReadOnlyList<AstExpr>)

AstFieldValue (abstract)
├── AstFieldBlock (Span, Block: AstBlock)            // nested { sub: 1, sub2: 2 }
├── AstFieldTyped (Span, TypeName: string, Value)    // bool = self.x > 0
└── AstFieldExpr  (Span, Value: AstExpr)             // any expression

AstTargetPath(Span, Segments: IReadOnlyList<AstTargetSegment>)
  .DisplayPath → "Game.abilities" or "Arena[Left].collapsed_for[Player1]"
AstTargetSegment(Span, Name: string, Indices: IReadOnlyList<AstExpr>)
```

## Expressions

```
AstExpr (abstract)
├── AstIntLit      (Value: int)
├── AstStringLit   (Value: string)
├── AstIdent       (Name: string)            // also true/false/None/Unbound/NoOp/Default
├── AstBinaryOp    (Op: string, Left, Right) // Op = "+", "-", "*", "==", "and", "∈", "×", "⊆", "∩", …
├── AstUnaryOp     (Op: string, Operand)     // "-" or "not" / "¬"
├── AstMemberAccess(Target, Member: string)
├── AstIndex       (Target, Indices: List<AstExpr>)
├── AstFunctionCall(Callee: AstExpr, Args: List<AstArg>)
├── AstLambda      (Parameters: List<string>, Body)
├── AstParen       (Elements: List<AstExpr>) // 1 elem → just that expr; >1 → tuple
├── AstListLit     (Elements)
├── AstRangeLit    (Start, End)
├── AstBraceExpr   (Entries: List<AstBraceEntry>)
├── AstIfExpr      (Condition, Then, Else)
├── AstSwitchExpr  (Scrutinee, Cases: List<AstSwitchCase>)
├── AstCondExpr    (Arms: List<AstCondArm>)
├── AstWhenExpr    (Predicate, Effect, Options: List<AstWhenOpt>)
└── AstLetExpr     (Variable, Value, Body)
```

## Supporting records

```
AstSwitchCase(Span, Label: string, Value: AstExpr)
AstCondArm   (Span, Predicate, Effect)
AstWhenOpt   (Span, Name: string, Value: AstExpr)

AstBraceEntry (abstract)
├── AstBraceField (Span, Field: AstField)    // "key: value" entry
└── AstBraceValue (Span, Value: AstExpr)     // bare element, e.g. {Player1, Player2}

AstArg (abstract)
├── AstArgPositional (Span, Value: AstExpr)          // foo(x)
├── AstArgNamed      (Span, Name: string, Value)     // foo(name: x)     — named arg
└── AstArgBinding    (Span, Name: string, Value)     // foo(name=x)      — pattern binding
```

## Named-arg vs pattern-binding distinction

`AstArgNamed` (`name: x`) and `AstArgBinding` (`name=x`) are
**syntactically different** and carry semantic intent:

- **Named args** are for constructor-style calls — `Triggered(on: …, effect: …)`.
- **Pattern bindings** are for event patterns — `Event.PhaseBegin(phase=Rise, player=p)`.

When the Evaluator builds `RtEventLit` from an `EmitEvent` call or when
the Interpreter's `TryMatchPattern` matches an `on:` clause, it accepts
both forms (treating them as "named payload field"). The distinction
matters mostly to human readers.

## Convenience accessors

Most nodes are plain records; the interesting ones are:

- `AstDeclaration.DisplayName` — the most-useful label for diagnostics.
- `AstTargetPath.DisplayPath` — dotted + bracketed segment join.
- `AstBinaryOp.Op` — the literal operator string, including Unicode
  (`∈`, `∧`, `∨`, `×`, `∩`, `⊆`).

## Walking the AST

- `Validator` uses a private `SemanticWalker` that switches on expression
  kind.
- `Evaluator.Eval` dispatches on the same hierarchy but produces
  `RtValue`s and side effects.
- `StateBuilder` walks only `AstEntityDecl.Body.Fields` and
  `AstEntityAugment.Value`.

When adding a new AST node type, grep for `case Ast` in these three
files to ensure all walkers are updated.
