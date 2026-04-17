# CCGNF Grammar Engine — Specification

*Target toolchain: **ANTLR 4.13.2**, C# runtime via the **Antlr4.Runtime.Standard 4.13.1** NuGet package. Implementation has not yet started; this document is the design spec.*

---

## 1. Purpose

CCGNF ("Cardgame Normal Form") is the encoding language used under `encoding/`. A complete CCGNF program declares a game's entities, abilities, macros, and cards. The engine specified here:

1. **Preprocesses** source files by expanding `define` macros.
2. **Parses** the preprocessed stream into an AST using an ANTLR-generated parser.
3. **Validates** the AST semantically (types, identifier resolution, ruling compliance).
4. **Interprets** the AST as a live game, responding to player inputs and emitting events.

The engine is a library; a host application (CLI, Godot integration, web front-end) drives it. The host is out of scope for this spec.

---

## 2. Architecture

```
    .ccgnf source files
           │
           ▼
┌────────────────────┐
│   Preprocessor     │   macro expansion, include resolution
└──────────┬─────────┘
           │  preprocessed token stream
           ▼
┌────────────────────┐
│   ANTLR Lexer      │
└──────────┬─────────┘
           │  tokens
           ▼
┌────────────────────┐
│   ANTLR Parser     │   generates concrete parse tree
└──────────┬─────────┘
           │  parse tree
           ▼
┌────────────────────┐
│   AST Builder      │   parse tree → typed AST
└──────────┬─────────┘
           │  AST
           ▼
┌────────────────────┐
│   Validator        │   identifier resolution, type checks,
│                    │   ruling-compliance, once_per_turn sanity
└──────────┬─────────┘
           │  validated AST + symbol tables
           ▼
┌────────────────────┐
│   Interpreter      │   runtime; loads into a GameState;
│                    │   drives event dispatch and ability firing
└────────────────────┘
```

All five stages are separate classes (library-wise), composable, independently testable.

---

## 3. Source file layout

A project consists of multiple `.ccgnf` files. The engine discovers them by scanning a root directory (default: `encoding/`) recursively. Files are processed in an order determined by an implicit dependency graph:

- **Level 0 — common:** `encoding/common/*.ccgnf`. Framework primitives. No game-specific references.
- **Level 1 — engine:** `encoding/engine/*.ccgnf`. Game entities, abilities, protocols. May reference Level 0.
- **Level 2 — cards/tokens:** `encoding/cards/*.ccgnf`, `encoding/engine/*tokens*.ccgnf`. May reference Levels 0 and 1.

Within a level, files are processed in lexical (filename) order. Two-digit numeric prefixes (`01-`, `02-`, …) enforce a deterministic order.

**Cycle detection:** any macro or identifier reference that points "up" a level (card referencing a card; engine referencing a card; common referencing engine) is a compile error.

---

## 4. Preprocessor

The preprocessor runs before the ANTLR parser. It is responsible for:

1. **Collecting `define` directives** across all source files into a single macro table.
2. **Expanding macro invocations** at their call sites.
3. **Resolving `#include`** statements (rare; used by tests).
4. **Stripping `//` comments** is *not* done here — the ANTLR lexer handles comments via `skip`. This keeps line numbers accurate for error reporting.

### 4.1 `define` syntax

```
define NAME(param1, param2, ...) = body
define NAME = body                      // zero-arg form
```

- `NAME` is an identifier (PascalCase for macros by convention).
- Parameters are untyped at the syntactic level; type-checking is deferred to the Validator.
- `body` is any CCGNF expression — a function call, an effect term, a block of abilities, etc. Bodies may span multiple lines; the terminator is either a blank line at column 0 or a following `define` directive (see §4.3 for lexical rules).
- A `define` at file-level creates a macro visible across all files at or below its level.
- Nested `define` (inside an entity, card, or expression) is **not supported.** Macros are file-top-level only.

### 4.2 Expansion semantics

Macros are expanded by **token-level substitution**, not string substitution. The preprocessor:

1. Tokenizes each source file.
2. For each token sequence matching `NAME` followed by `(args)`, replaces the sequence with the macro body's tokens, with parameter tokens substituted for argument tokens.
3. Expansion is **recursive**: a macro body may invoke other macros; expansion continues until a fixed point. **Self-reference is a compile error** (detected by maintaining an expansion stack during expansion).
4. **Hygiene:** local identifiers introduced by macro bodies are *not* renamed. Macro authors are responsible for not shadowing caller-scope identifiers. This is a deliberate simplification; if practice shows it to be error-prone, a future revision can add gensym support.

### 4.3 Lexical framing

Macro bodies can be multi-line. The preprocessor's macro-body parser uses bracket/paren matching to find the end of a body — a body ends when every `(`, `[`, `{` opened inside the body has been closed, and the next token is either EOF, a blank line, or another `define`.

### 4.4 Error reporting

Preprocessor errors (undefined macro, wrong arity, cycle) carry the *original file path + line number*, not the post-expansion position. This requires the preprocessor to emit a **source map** alongside the expanded token stream.

---

## 5. ANTLR grammar

The grammar is a single combined grammar file: `Ccgnf.g4`. It lives in the `Grammar` project alongside the generated C# sources.

### 5.1 Lexer fragment (sketch)

```antlr
lexer grammar CcgnfLexer;

// Keywords
ENTITY       : 'Entity' ;
CARD         : 'Card' ;
TOKEN        : 'Token' ;
DEFINE       : 'define' ;
IF           : 'If' ;
SWITCH       : 'Switch' ;
COND         : 'Cond' ;
WHEN         : 'When' ;
LET          : 'let' ;
IN_KW        : 'in' ;
TRUE         : 'true' ;
FALSE        : 'false' ;
NONE         : 'None' ;
UNBOUND      : 'Unbound' ;
DEFAULT_KW   : 'Default' ;
NOOP         : 'NoOp' ;

// Operators and punctuation
ARROW        : '->' ;
PLUS_EQ      : '+=' ;
LBRACE       : '{' ;
RBRACE       : '}' ;
LBRACK       : '[' ;
RBRACK       : ']' ;
LPAREN       : '(' ;
RPAREN       : ')' ;
COMMA        : ',' ;
COLON        : ':' ;
DOT          : '.' ;
SEMI         : ';' ;
EQ           : '=' ;
EQ_EQ        : '==' ;
NEQ          : '!=' ;
LE           : '<=' ;
GE           : '>=' ;
LT           : '<' ;
GT           : '>' ;
PLUS         : '+' ;
MINUS        : '-' ;
STAR         : '*' ;
SLASH        : '/' ;
PIPE         : '|' ;

// Unicode operators accepted as equivalents
AND          : 'and' | '\u2227' ;   // ∧
OR           : 'or'  | '\u2228' ;   // ∨
NOT          : 'not' | '\u00AC' ;   // ¬
ELEMENT_OF   : '\u2208' ;           // ∈
CARTESIAN    : '\u00D7' ;           // ×
INTERSECT    : '\u2229' ;           // ∩
SUBSETEQ     : '\u2286' ;           // ⊆

// Literals
IDENT        : [a-zA-Z_] [a-zA-Z_0-9]* ;
INT_LIT      : [0-9]+ ;
STRING_LIT   : '"' (~["\\\r\n] | '\\' .)* '"' ;

// Trivia
LINE_COMMENT : '//' ~[\r\n]* -> skip ;
BLOCK_COMMENT: '/*' .*? '*/' -> skip ;
WS           : [ \t\r\n]+ -> skip ;
```

### 5.2 Parser (sketch)

```antlr
parser grammar CcgnfParser;
options { tokenVocab = CcgnfLexer; }

file
    : declaration* EOF
    ;

declaration
    : entityDecl
    | entityAugment
    | cardDecl
    | tokenDecl
    | macroDef
    ;

// --- Top-level declarations ---

entityDecl
    : ENTITY IDENT (LBRACK expr RBRACK)? LBRACE fieldList RBRACE
    ;

cardDecl
    : CARD IDENT LBRACE fieldList RBRACE
    ;

tokenDecl
    : TOKEN IDENT LBRACE fieldList RBRACE
    ;

entityAugment
    : qualifiedName PLUS_EQ expr           // e.g., Game.abilities += Triggered(...)
    ;

macroDef
    : DEFINE IDENT (LPAREN paramList RPAREN)? EQ expr
    ;

fieldList
    : (field (COMMA field)* COMMA?)?
    ;

field
    : IDENT COLON fieldValue
    ;

fieldValue
    : expr
    | LBRACE fieldList RBRACE            // nested block (e.g., characteristics: { ... })
    ;

// --- Expressions ---

expr
    : orExpr
    ;

orExpr   : andExpr    (OR andExpr)* ;
andExpr  : notExpr    (AND notExpr)* ;
notExpr  : NOT notExpr | relExpr ;
relExpr  : addExpr ((EQ_EQ | NEQ | LE | GE | LT | GT | ELEMENT_OF | SUBSETEQ) addExpr)? ;
addExpr  : mulExpr ((PLUS | MINUS) mulExpr)* ;
mulExpr  : unaryExpr ((STAR | SLASH | CARTESIAN | INTERSECT) unaryExpr)* ;
unaryExpr: MINUS unaryExpr | postfixExpr ;

postfixExpr
    : atom trailer*
    ;

trailer
    : DOT IDENT                             // property access
    | LBRACK expr RBRACK                    // index
    | LPAREN argList? RPAREN                // call
    ;

atom
    : literal
    | setLiteral
    | listLiteral
    | lambda
    | ifExpr
    | switchExpr
    | condExpr
    | whenExpr
    | letExpr
    | IDENT
    | LPAREN expr RPAREN
    ;

literal       : INT_LIT | STRING_LIT | TRUE | FALSE | NONE | UNBOUND | NOOP | DEFAULT_KW ;
setLiteral    : LBRACE (expr (COMMA expr)*)? RBRACE ;
listLiteral   : LBRACK (expr (COMMA expr)*)? RBRACK ;
lambda        : IDENT ARROW expr ;                           // single-param
              | LPAREN paramList RPAREN ARROW expr ;         // multi-param
ifExpr        : IF LPAREN expr COMMA expr COMMA expr RPAREN ;
switchExpr    : SWITCH LPAREN expr COMMA LBRACE switchCase (COMMA switchCase)* RBRACE RPAREN ;
switchCase    : IDENT COLON expr ;
condExpr      : COND LPAREN LBRACK condArm (COMMA condArm)* RBRACK RPAREN ;
condArm       : LPAREN expr COMMA expr RPAREN ;
whenExpr      : WHEN LPAREN expr COMMA expr (COMMA whenOpt)* RPAREN ;
whenOpt       : IDENT COLON expr ;                           // check_at, apply_at, etc.
letExpr       : LET IDENT EQ expr IN_KW expr ;

argList       : arg (COMMA arg)* ;
arg           : (IDENT COLON)? expr ;                        // named or positional
paramList     : IDENT (COMMA IDENT)* ;
qualifiedName : IDENT (DOT IDENT)+ ;
```

### 5.3 Grammar notes

- **Named arguments are first-class.** `Target(selector: foo, chooser: p, effect: bar)` is the primary style; positional args exist for single-argument cases.
- **Lambdas** use `x -> body` syntax. Multi-parameter lambdas wrap the list in parens: `(x, y) -> body`.
- **Operator precedence** (tightest to loosest): postfix > unary > mul > add > rel > not > and > or.
- **Set vs block disambiguation:** `{}` is a set/block literal only when inside an expression position. At the top level of a field value, `fieldValue: { … }` is a nested block if the `…` contains `IDENT COLON …` pairs, else a set. The parser resolves this by trying the block rule first, falling back to set.

### 5.4 Forbidden constructs

The grammar does *not* permit:

- `Procedure NAME(args): …` blocks. Procedures were removed from the encoding in favor of event-driven abilities. A file containing this token produces a parse error.
- Top-level imperative statements. Only declarations (entity, card, token, macro, augment) can appear at file top level.
- Numeric step labels like `Step 1 — …` outside of `//` comments. These were artifacts of the old procedural encoding.

Enforcing these in the grammar (rather than in the Validator) catches drift earlier and preserves the "no procedures in encoding" invariant.

---

## 6. AST model

The AST is a typed tree of C# records, one type per grammar rule of interest. Every AST node carries:

- A **source span** (`FilePath`, `StartLine`, `StartColumn`, `EndLine`, `EndColumn`).
- A **pre-expansion span**, preserved by the preprocessor's source map.

### 6.1 Node taxonomy

```
File
├── Declaration
│   ├── EntityDecl           (name, kind, characteristics, fields, abilities, ...)
│   ├── CardDecl             (name, factions, type, cost, stats, keywords, abilities, ...)
│   ├── TokenDecl            (similar to CardDecl but distinguished for tooling)
│   ├── EntityAugment        (target qualified name, operation, rhs expr)
│   └── MacroDef             (handled by preprocessor, not present in post-preprocess AST)
└── Expression
    ├── FunctionCall         (callee, positional args, named args)
    ├── Lambda               (params, body)
    ├── BinaryOp / UnaryOp
    ├── PostfixChain         (base, trailers)
    ├── IfExpr / SwitchExpr / CondExpr / WhenExpr / LetExpr
    ├── SetLiteral / ListLiteral
    ├── Literal              (int, string, bool, None, Unbound, NoOp, Default)
    ├── Identifier
    └── QualifiedName
```

### 6.2 Typed envelopes

A post-validation AST node carries an inferred `TypeTag`:

- **Effect** — something that modifies game state when applied.
- **Predicate** — a boolean-valued expression.
- **Selector** — a predicate used for binding (`x -> x.cost <= 3`).
- **Value** — integer, string, identifier, set, list.
- **AbilitySpec** — an ability declaration, uninstantiated.
- **EntityRef** — reference to an entity.

`FunctionCall` nodes carry a `ResolvedBuiltin` or `ResolvedUserDefined` reference once the Validator has matched the callee.

---

## 7. Validator

The Validator walks the AST after parse and produces symbol tables plus error/warning diagnostics. Responsibilities:

1. **Identifier resolution.** Every `IDENT` and `QualifiedName` is resolved to a declared entity, card, macro, parameter binding, or builtin.
2. **Arity and named-argument checks.** Each function call is verified against the callee's signature.
3. **Type checks** (lightweight; the language is not fully statically typed). E.g., a `ForEach(pred, effect)` requires an effect in the second position; a `Guard(predicate, on_fail: …)` requires a predicate.
4. **Ruling compliance.** Cross-references `encoding/engine/00-rulings.ccgnf`. Example: R-5 says Debt uses `printed_cost`, not `effective_cost`. The Validator flags any ability that computes Debt against `effective_cost`.
5. **Once-per-turn sanity.** Abilities annotated `once_per_turn: true` must have no other hand-rolled `used_this_turn` counter manipulation; abilities without the annotation must not reference `self.counters.*_used_this_turn`.
6. **Forbidden-construct scan.** Redundant with the grammar in the nominal case, but double-checks after macro expansion.

Diagnostics carry severity (`Error | Warning | Info`) and a source span. The Interpreter refuses to execute a program with any `Error` diagnostics.

---

## 8. Interpreter

The Interpreter is the runtime that executes a validated AST. It owns a `GameState` (a mutable tree of entity instances) and a `Scheduler` (the event queue and timing-window machinery).

### 8.1 Core types (C# sketch)

```csharp
public sealed class GameState {
    public Entity Game { get; }
    public IReadOnlyList<Entity> Players { get; }
    public EventQueue PendingEvents { get; }
    public TimingWindowStack ActiveWindows { get; }
    // ... zones, standards, standings, etc.
}

public abstract class Entity {
    public EntityId Id { get; }
    public string Kind { get; }
    public Dictionary<string, object> Characteristics { get; }
    public Dictionary<string, int> Counters { get; }
    public HashSet<string> Tags { get; }
    public List<AbilityInstance> Abilities { get; }
    public Entity? Owner { get; set; }
    public Entity? Controller { get; set; }
    public Zone? Zone { get; set; }
}

public abstract class AbilityInstance {
    public AbilityKind Kind { get; }            // Static, Triggered, Replacement, Activated, OnResolve
    public AbilitySpec Source { get; }          // points back into the AST
    public Entity Owner { get; }
    public bool OncePerTurn { get; set; }
    public int UsedThisTurn { get; set; }
    public abstract bool Matches(Event e, GameState state);
    public abstract IEnumerable<Effect> Apply(Event e, GameState state);
}

public abstract class Effect {
    public abstract void Execute(GameState state);
}
```

### 8.2 Event loop

```
loop:
    if ActiveWindows.Top has pending triggers:
        fire them in order (active player's first, then opponent's)
    else if PendingEvents not empty:
        dequeue e
        for each Replacement ability matching e: fire, possibly replacing e
        if e still pending: commit e, then fire matching Triggered abilities
    else if game over:
        return final result
    else:
        await host input (a player action, usually an Activated ability invocation)
```

### 8.3 State-based actions

After every event resolution and at every window boundary, the Interpreter runs the SBA pass (defined in `encoding/engine/06-sba.ccgnf`). The SBAs are themselves Static abilities on Game; the Interpreter doesn't special-case them beyond scheduling.

### 8.4 Determinism

The Interpreter is deterministic given:
- A seeded RNG (for shuffle, coin flip, random-exile).
- A queue of host inputs (player choices in order).

These two inputs, plus the validated AST, reproduce any game exactly. This is a hard requirement for replay and test fixture support.

---

## 9. Testing

Three layers:

1. **Grammar tests.** Each fixture pair is `(input.ccgnf, expected-parse.json)` or `(input.ccgnf, expected-error.txt)`. Failing fixtures are the grammar's regression suite.
2. **Validator tests.** Fixtures that test each diagnostic rule — e.g., a card that references `effective_cost` for Debt should emit the R-5 error.
3. **Interpreter tests.** Fixtures that specify `(initial GameState, input sequence, expected final GameState)`. The fixture language is YAML; the test harness loads, runs the Interpreter, and diffs. Dozens of these cover the play chain, Clash, SBAs, and each card's printed effect.

All three layers use NUnit 3 (or XUnit; pick one and commit). Fixtures live under `tests/fixtures/`.

---

## 10. Project layout (implementation)

```
src/
  Ccgnf.Grammar/             # ANTLR-generated lexer/parser + grammar file
    Ccgnf.g4
    CcgnfLexer.g4            (if split)
    CcgnfParser.g4           (if split)
    generated/               # ANTLR output (gitignored, regenerated on build)
  Ccgnf.Preprocessor/
    MacroTable.cs
    Preprocessor.cs
    SourceMap.cs
  Ccgnf.Ast/
    AstNode.cs + records
    AstBuilder.cs            # parse tree → AST
  Ccgnf.Validator/
    Validator.cs
    Diagnostics.cs
    Rulings.cs               # R-1..R-6 compiled checks
  Ccgnf.Interpreter/
    GameState.cs
    Entity.cs, Zone.cs, AbilityInstance.cs, Effect.cs
    EventQueue.cs, TimingWindow.cs, Scheduler.cs
    Builtins.cs              # Sequence, ForEach, If, DealDamage, …
  Ccgnf.Cli/                 # reference CLI; validates a project, prints diagnostics
tests/
  Ccgnf.Grammar.Tests/
  Ccgnf.Validator.Tests/
  Ccgnf.Interpreter.Tests/
  fixtures/
```

### 10.1 Build

`dotnet` with an `Antlr4.CodeGenerator` MSBuild task, pointed at the 4.13.2 ANTLR tool JAR (vendored under `tools/` or downloaded via the task). Runtime is `Antlr4.Runtime.Standard 4.13.1` from NuGet. Target framework: `net8.0` unless a downstream Godot integration pins otherwise.

### 10.2 Regeneration

The ANTLR-generated C# sources go under `src/Ccgnf.Grammar/generated/` and are gitignored. CI regenerates on every build. Local dev regenerates via `dotnet build`.

---

## 11. Open questions

These are for the implementation phase, not blockers for this spec:

- **Error recovery** in the parser. ANTLR4's default is good for most constructs; we may want custom recovery for the `entityAugment` and `macroDef` productions to give better error messages when authors omit a comma.
- **Incremental parsing.** Initial version parses the whole project on every run. If authoring latency becomes painful, add file-level caching keyed by content hash.
- **Godot integration.** Whether the runtime runs inside Godot as a GDExtension, or as an external process that Godot talks to over JSON. The parent directory is `GodotProj/`, so this will come up; not this spec's problem.
- **Card-authoring DX.** The CCGNF syntax is designer-centric. A card-authoring front-end (spreadsheet, GUI form) that emits CCGNF is plausible post-launch. Out of scope here.

---

*End of grammar engine spec. Implementation proceeds from `src/Ccgnf.Grammar/Ccgnf.g4`.*
