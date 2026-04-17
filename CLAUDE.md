# Project Notes for LLM Agents

## Default game target

**Resonance** (also "the echo game") is the active, canonical game for this project. Unless a prompt explicitly names a different target, assume:

- Rules live in `design/GameRules.md` and `design/Supplement.md`.
- The machine-readable encoding lives under `encoding/` (see `encoding/README.md`).
- Resonance uses five factions (EMBER, BULWARK, TIDE, THORN, HOLLOW), three Arenas, Conduits, a five-Echo Resonance Field, the Banner derivative, and the Aether-refresh-not-accumulate resource model.

### Legacy game

**Conduit** is a prior iteration of this project's design that we have since abandoned in favor of Resonance. Its docs (`RULES.md`, `CARDS.md`, `FACTIONS.md`, `7thColorChatLog.md`) live under `legacy/conduit/`. Do not assume Conduit terminology or mechanics when answering questions about "the card game." If a user asks about Conduit by name, the files are there; otherwise treat them as archival only.

## Build conventions

- **Canonical build definition: SDK-style `.csproj` files.** All C# project metadata lives in these. Do not introduce legacy `.csproj` schemas, `packages.config`, or out-of-SDK build logic.
- **Primary developer interface: `Makefile` at repo root.** Wraps `dotnet` CLI commands (`make build`, `make test`, `make clean`, etc.). Agents and humans should prefer `make <target>` over raw `dotnet` invocations so CI and local dev stay in sync.
- **Linux is the first-class build target.** GitHub Actions runs Ubuntu. Developers on other platforms may use Linux under WSL or a VM; keeping Linux-green is non-negotiable.
- **Visual Studio compatibility is a secondary convenience.** A `.sln` file is maintained so VS/Rider can open the project, but Visual Studio is not the build authority. CI never invokes `msbuild` directly.

## Toolchain

### Cardgame Normal Form Grammar Engine: ANTLR 4.13.2, C# target via the Antlr4.Runtime.Standard 4.13.1 NuGet package

The CCGNF encoding (`.ccgnf` files under `encoding/`) will be parsed and interpreted by a purpose-built engine. See `grammar/GrammarSpec.md` for the full spec. Tooling and implementation have not yet been built.

### Host targets

The engine library has three first-class host targets, all C#:

- **CLI** — validate projects and run fixture games from the command line.
- **REST API** — ASP.NET Core service; exposes validation and game-session endpoints over HTTP for non-.NET front-ends.
- **Godot** — in-process integration for a Godot 4.x C# front-end; runs the interpreter directly, no IPC.

### Logging

All library projects log via `Microsoft.Extensions.Logging.Abstractions` (`ILogger<T>` constructor injection). No library depends on a concrete logging provider. Each host composes its own: CLI uses `Microsoft.Extensions.Logging.Console`, REST uses ASP.NET Core defaults (optionally Serilog), Godot uses a custom `ILoggerProvider` that routes to `GD.Print` / `GD.PushWarning` / `GD.PushError`. See `grammar/GrammarSpec.md` §9 for conventions.

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
