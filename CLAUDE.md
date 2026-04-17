# Project Notes for LLM Agents

## Default game target

**Resonance** (also "the echo game") is the active, canonical game for this project. Unless a prompt explicitly names a different target, assume:

- Rules live in `design/GameRules.md` and `design/Supplement.md`.
- The machine-readable encoding lives under `encoding/` (see `encoding/README.md`).
- Resonance uses five factions (EMBER, BULWARK, TIDE, THORN, HOLLOW), three Arenas, Conduits, a five-Echo Resonance Field, the Banner derivative, and the Aether-refresh-not-accumulate resource model.

### Legacy game

**Conduit** is a prior iteration of this project's design that we have since abandoned in favor of Resonance. Its docs (`RULES.md`, `CARDS.md`, `FACTIONS.md`, `7thColorChatLog.md`) live under `legacy/conduit/`. Do not assume Conduit terminology or mechanics when answering questions about "the card game." If a user asks about Conduit by name, the files are there; otherwise treat them as archival only.

## Toolchain

### Cardgame Normal Form Grammar Engine: ANTLR 4.13.2, C# target via the Antlr4.Runtime.Standard 4.13.1 NuGet package

The CCGNF encoding (`.ccgnf` files under `encoding/`) will be parsed and interpreted by a purpose-built engine. See `grammar/GrammarSpec.md` for the full spec. Tooling and implementation have not yet been built.

## File layout

```
design/          Human-readable rules and card supplements (Resonance).
encoding/        Machine-readable .ccgnf source files.
  common/        Framework primitives (schema, lifecycle, combinators).
  engine/        Resonance engine (entities, phases, play chain, Clash, SBAs).
  cards/         Card definitions, one file per faction.
grammar/         Spec for the ANTLR grammar, preprocessor, and interpreter.
legacy/conduit/  Archived prior iteration.
```

## Conventions for edits

- Keep `design/` (human) and `encoding/` (machine) in sync. When rules change, update both or flag the drift explicitly.
- `.ccgnf` files use `//` for comments; they are intended to be both human-readable and grammar-parseable.
- Rulings (R-1 through R-6) in `encoding/engine/00-rulings.ccgnf` are authoritative design decisions that resolve ambiguities in `design/GameRules.md`. If GameRules changes to address a ruling, remove the ruling from the encoding.
- Do not reintroduce `Procedure …` blocks in `encoding/`. Game logic is expressed as abilities attached to entities, events, and derived characteristics — never as top-level procedures. See `grammar/GrammarSpec.md` §"Forbidden constructs."

## Working with this repo

- Commits are one logical change each. Keep design changes separate from encoding changes when feasible.
- Before editing `.ccgnf` files, check whether the same rule is described in `design/GameRules.md` — the design doc is the source of truth for intent; the encoding is the source of truth for mechanics.
- Remote is local only; no push destination is configured.
