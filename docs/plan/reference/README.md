# Reference digests

Per-module type catalogs. Read these *instead of* opening the files when
you only need to know "what's there and how to call it". If behavior
surprises you, fall back to the code — these docs age.

| Doc | Covers |
|-----|--------|
| [ccgnf-lib.md](ccgnf-lib.md) | Preprocessor · Parser · AstBuilder · Validator · Diagnostics |
| [interpreter.md](interpreter.md) | GameState · Entity · Zone · Evaluator · StateBuilder · Scheduler · Interpreter · ProjectLoader |
| [builtins.md](builtins.md) | Every builtin dispatched through `Builtins.TryDispatch` |
| [ast-nodes.md](ast-nodes.md) | AST record hierarchy |
| [rest.md](rest.md) | REST host composition (endpoints → DTOs → sessions) |

## Convention

Each doc lists types in the order they appear in their home directory's
alphabetical file order. For each type:

- **Location** — `path/to/file.cs:line`.
- **Purpose** — one line.
- **Public surface** — constructors, public/internal methods and
  properties relevant to callers. Private helpers omitted unless they
  implement a named algorithm.
- **Key invariants** — anything surprising the code depends on.

If a type's responsibilities grow, split it into its own file and update
the digest.
