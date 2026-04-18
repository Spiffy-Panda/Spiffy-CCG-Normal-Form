# Spiffy CCG Normal Form

> A declarative encoding language for collectible card game rules, and a C# grammar engine that interprets it. Compose a game's entities, abilities, cards, and turn structure as events and data — no imperative glue. Ships with **Resonance**, a complete original CCG, as the reference encoding.

![test](https://github.com/Spiffy-Panda/Spiffy-CCG-Normal-Form/actions/workflows/test.yml/badge.svg)

---

## What is CCGNF?

**Cardgame Normal Form** (CCGNF) is a domain-specific language for describing the full rules and card content of a collectible card game. It is:

- **Declarative** — a game is a set of entities, abilities, and event triggers. No `Procedure` blocks, no top-level imperative statements.
- **Compositional** — every keyword is a macro. Every ability is one of five kinds (`Static`, `Triggered`, `OnResolve`, `Replacement`, `Activated`). Six primitives cover the whole surface: Entity, Zone, Event, Effect, Ability, Predicate.
- **Host-agnostic** — the grammar engine is a .NET library with three first-class host targets (CLI, REST API, Godot). Each host plugs in its own logging, serialization, and I/O.
- **Author-readable** — `//` comments, Unicode or ASCII operators, named arguments, and a consistent YAML-ish / S-expression-ish blend.

Files use the `.ccgnf` extension. A small example from Resonance:

```ccgnf
Card Spark {
  factions: {EMBER}, type: Maneuver, cost: 1, rarity: C
  abilities: [
    OnResolve(
      Target(t -> t.kind ∈ {Unit, Conduit},
        Tiers([
          (Resonance(EMBER, 3), DealDamage(target, 3)),
          (Default,             DealDamage(target, 2))
        ])))
  ]
  // text: Deal 2 damage to a Unit or Conduit. EMBER 3: Deal 3 instead.
}
```

Every construct in that card — the lambda, the set membership, the tier table, the conditional tier effects, the positional and named arguments — is a grammar primitive. There is no special "tier" mechanic in the engine; `Tiers` is a macro that expands to standard `ChooseHighest` semantics over predicates.

See [`grammar/GrammarSpec.md`](grammar/GrammarSpec.md) for the full language specification.

---

## Status

CCGNF is pre-alpha. The preprocessor → parser → AST → validator → interpreter pipeline executes the reference encoding through Setup and into the first Round-1 Rise phase.

| Component                                  | State                         |
|--------------------------------------------|-------------------------------|
| CCGNF grammar specification                | **Complete** (§1–§12)         |
| CCGNF engine — preprocessor                | **Working** — v1              |
| CCGNF engine — ANTLR grammar               | **Working** — v1              |
| CCGNF engine — AST builder                 | **Working** — v1 (typed records over the parse tree) |
| CCGNF engine — validator                   | **Working** — v1 (duplicate decls, builtin arity, R-5) |
| CCGNF engine — interpreter                 | **Skeleton** — v1 (Setup → first Round-1 Rise; seeded RNG; pre-sequenced inputs) |
| E2E grammar coverage fixture               | **Complete**; parses clean    |
| Resonance rules (design docs)              | **Complete**                  |
| Resonance CCGNF encoding                   | **Complete**; all 22 files parse cleanly under CI |
| CLI host (`Ccgnf.Cli`)                     | **Working** — preprocess + parse; `--run` executes v1 interpreter |
| REST host (`Ccgnf.Rest`)                   | Specified; not scaffolded     |
| Godot host (`Ccgnf.Godot`)                 | Specified; not scaffolded     |
| Solution + test project                    | **Working** — 99 tests green  |
| Linux CI (GitHub Actions)                  | **Wired**                     |

"v1" means the component implements the core path specified in `grammar/GrammarSpec.md` against the e2e coverage fixture, with known gaps documented in §12 Open questions. Future passes will add source-map support, ASCII-`in` set-membership, and richer error recovery.

---

## Quick start

This repo builds on any platform with .NET 8 SDK. Linux is the first-class target; the Makefile is the canonical developer interface.

```bash
# Restore, build, test.
make ci

# Individual steps.
make restore
make build
make test
```

On Windows, either use WSL (see `CLAUDE.md` for the `DOTNET=dotnet.exe` override) or invoke `dotnet` directly:

```powershell
dotnet build Ccgnf.sln
dotnet test  Ccgnf.sln
```

See `make help` for all targets.

---

## The example game: Resonance

**Resonance** is an original two-player CCG. It is the canonical example in this repo — it exercises every CCGNF primitive in a realistic design, and its cards are the test corpus the engine will validate and execute.

**One-sentence pitch.** Two players conduct rival currents across three battlefields, seeking to destroy two of three Conduits; each card you play leaves an Echo, and your last five Echoes — your **Resonance Field** — determine which tier of each card's effects fires.

Mechanics at a glance:

- Five factions: EMBER, BULWARK, TIDE, THORN, HOLLOW.
- Soft faction pressure: no declared deck identity; off-faction plays push on-faction Echoes out of your Field, so splashing rarely reaches Peak.
- Three Arenas (Left, Center, Right). Win by destroying 2 of opponent's 3 Conduits.
- No tap-to-attack. Simultaneous Clash. Units do not die in combat.
- Fixed Aether scaling (no land draws).

Full rules and supplement:

- [`design/GameRules.md`](design/GameRules.md) — canonical rules document (victory conditions, turn structure, Resonance system, combat, keywords, formats).
- [`design/Supplement.md`](design/Supplement.md) — faction identity detail, example cards (all rarities for each faction), rarity guidelines, draft environment targets, open design questions.

The machine-readable version lives in [`encoding/`](encoding/README.md).

---

## Targets

All three target hosts are C#. Each consumes the same `Ccgnf.Interpreter` library; they differ only in composition and I/O.

### CLI — `src/Ccgnf.Cli`
Primary developer-facing target. Validates a CCGNF project, runs fixture games, prints diagnostics. Uses `Microsoft.Extensions.Logging.Console`. `--run` drives the v1 interpreter through Setup into the first Round-1 Rise against a list of `.ccgnf` files. **Status: skeleton.**

### REST — `Ccgnf.Rest` *(planned)*
ASP.NET Core service exposing validation and game-session endpoints over HTTP. Designed for non-.NET front-ends (web clients, Python bots, tournament platforms). Sessions are in-memory by default. **Status: specified, not scaffolded.**

Endpoint shape (from the spec):
```
POST /api/projects/validate
POST /api/sessions
POST /api/sessions/{id}/actions
GET  /api/sessions/{id}/state
GET  /api/sessions/{id}/events   (Server-Sent Events)
```

### Godot — `Ccgnf.Godot` *(planned)*
Thin shim consumed by a Godot 4.x C# project. The engine runs in-process as a library — no IPC, no subprocess. A custom `ILoggerProvider` routes logs to `GD.Print` / `GD.PushWarning` / `GD.PushError`. **Status: specified, not scaffolded.**

See [`grammar/GrammarSpec.md`](grammar/GrammarSpec.md) §11.3 for each host's intended composition.

---

## Repository layout

```
design/         Human-readable Resonance rules (GameRules, Supplement).
encoding/       Machine-readable CCGNF source files for Resonance.
  common/       Framework primitives (conventions, schema, lifecycle,
                combinators). Reusable across any CCG.
  engine/       Resonance engine: rulings, macros, entities, setup,
                turn structure, SBAs, play chain, Clash, tokens.
  cards/        One .ccgnf file per faction: ember, bulwark, tide,
                thorn, hollow, dual, neutral.
grammar/        GrammarSpec.md — full engine specification (preprocessor,
                ANTLR grammar, AST, validator, interpreter, logging,
                project layout, testing strategy).
legacy/         Archived prior design iterations (Conduit); do not
                treat as valid Resonance.
src/
  Ccgnf/        The engine library (skeleton).
  Ccgnf.Cli/    The CLI host (skeleton).
tests/
  Ccgnf.Tests/  xUnit test project.
    fixtures/   CCGNF source files used as test inputs — notably
                e2e-grammar-coverage.ccgnf, which exercises every
                primitive in the grammar.
.github/
  workflows/    CI (GitHub Actions, Ubuntu).
```

More detail in [`encoding/README.md`](encoding/README.md).

---

## Design philosophy

A few invariants the engine and encoding are committed to:

1. **No procedures in game logic.** Every rule is an ability on an entity, or a derived characteristic, or an event-driven transition. If you find yourself writing a top-level `for` loop to express a rule, the rule is under-decomposed. The grammar rejects `Procedure` as a keyword.
2. **Rulings are first-class.** Ambiguities between the human design document (`design/`) and the machine encoding (`encoding/`) are resolved in `encoding/engine/00-rulings.ccgnf` with identifiers R-1, R-2, etc. The Validator cross-references these; any code that violates a ruling fails compilation.
3. **Host independence.** Library projects depend only on `Microsoft.Extensions.Logging.Abstractions`. Concrete providers (console, ASP.NET Core, Godot) are a host concern.
4. **Linux is the build authority.** `make ci` on Ubuntu is ground truth; Visual Studio is a convenience.
5. **Determinism.** The interpreter takes a seeded RNG and a queue of host inputs. Same inputs, same outputs. Replay and test fixtures depend on this.

---

## Documentation index

- [`design/GameRules.md`](design/GameRules.md) — Resonance rules (human).
- [`design/Supplement.md`](design/Supplement.md) — Resonance cards and design notes.
- [`encoding/README.md`](encoding/README.md) — CCGNF source file layout for Resonance.
- [`encoding/DESIGN-NOTES.md`](encoding/DESIGN-NOTES.md) — encoding design choices and remaining open questions.
- [`grammar/GrammarSpec.md`](grammar/GrammarSpec.md) — **the engine specification** (architecture, preprocessor, ANTLR grammar, AST, validator, interpreter, logging, testing).
- [`CLAUDE.md`](CLAUDE.md) — conventions for agents and humans working in this repo.
- [`legacy/conduit/README.md`](legacy/conduit/README.md) — archived prior iteration.

---

## Contributing

There is no published contribution process yet. The repo is in a specify-before-implement phase; the next meaningful work is to begin the grammar engine implementation against the spec in `grammar/GrammarSpec.md`. Issues and PRs will open once implementation is live.

If you are adding to Resonance itself (new cards, balance changes, keyword tweaks):

1. Update `design/GameRules.md` or `design/Supplement.md` — these are the intent source of truth.
2. Mirror the change in the relevant file under `encoding/engine/` or `encoding/cards/`.
3. If the change resolves an existing Ruling (R-1 through R-6), remove the ruling from `encoding/engine/00-rulings.ccgnf`.
4. If the change introduces new ambiguity, add a new ruling.
5. `make test` locally before pushing.

Conventions are documented in [`CLAUDE.md`](CLAUDE.md).
