# Steps

Ordered delivery plan for the web-app arc. Each step has its own file
covering goals, code changes, tests, and a commit template. Steps land
independently; after each, CI stays green, the README status table
updates if a threshold crossed, and an entry goes in
[../devlog.md](../devlog.md).

## Order

| # | File | Blocks | Summary |
|---|------|--------|---------|
| 1 | [01-vite-scaffold.md](01-vite-scaffold.md) | — | Create `web/`, wire into Makefile, migrate the playground under `#/interpreter`. No feature changes. |
| 2 | [02-cards-project-endpoints.md](02-cards-project-endpoints.md) | 1 | Add `GET /api/cards` and `GET /api/project` — the read-only data plane. |
| 3 | [03-cards-rules-page.md](03-cards-rules-page.md) | 1, 2 | Build the Cards page (faceted list + detail) and the Rules tab (declaration tree). |
| 4 | [04-decks-page.md](04-decks-page.md) | 1, 2, 3 | Deck construction with format selector and Draft mock pool. |
| 5 | [05-raw-view.md](05-raw-view.md) | 1, 2 | Raw file-tree view with syntax highlighting. |
| 6 | [06-rooms.md](06-rooms.md) | 1, 2 | Rooms layer, Lobby + Tabletop pages, SSE. |
| 7 | [07-playtest-mvp.md](07-playtest-mvp.md) | 1–6 | Playtest MVP: AST humanizer, preset decks, card/board rendering, CPU seat, export. |
| 8 | [08-full-game.md](08-full-game.md) | 1–7 | First full human + CPU game played through the Play tab — real turns, card play, Clash, Conduit collapse, GameEnd. |
| 9 | [09-godot-client.md](09-godot-client.md) | 1–7 | Godot 4.x C# front-end against the same room REST API. Web and Godot clients share rooms. |
| 10 | [10-long-term-ai.md](10-long-term-ai.md) | 7+ | Utility-AI-first CPU with a top-level BT shell. No look-ahead search. |
| 10.2 | [10.2-long-term-ai-plan.md](10.2-long-term-ai-plan.md) | 10 | Concrete 10 plan: `Ccgnf.Bots`, UtilityBot + phase-BT + sticky intent, `/api/ai/*`, deck archetypes, tournament harness, `#/ai` editor. **Shipped.** |
| 11 | [11-humanizer-templates.md](11-humanizer-templates.md) | 3+ | Move the per-builtin humanizer table to a data-driven, user-overridable template library. |
| 12.0 | [12.0-balance-baseline.md](12.0-balance-baseline.md) | 10.2 | Balance arc opener: measurement-only baseline tournament runs, no tuning. |
| 12.1 | [12.1-ai-floor-fix.md](12.1-ai-floor-fix.md) | 12.0 | AI floor rule (`conduit_softness ≥ threat_avoidance` under pushing / lethal_check). Also the iteration point re-run after every later step. |
| 12.2 | [12.2-engine-sanity-pass.md](12.2-engine-sanity-pass.md) | 12.1 | Rule audit: min-turns-to-lethal + draw-guard catalogue. Engine knobs documented, touched only if necessary. |
| 12.3 | [12.3-card-threat-audit.md](12.3-card-threat-audit.md) | 12.2 | Per-card tag pass + per-faction closer/setup/disruption/filler ratios. Author closers where gaps exist. |
| 12.4 | [12.4-deck-construction.md](12.4-deck-construction.md) | 12.3 | Reference decks swap fillers for cards from the cleaned pool. Archetype tags and `suggested_ai` pinned. |
| 12.5 | [12.5-matched-ai-tune.md](12.5-matched-ai-tune.md) | 12.4 | Re-converge matched AI weights under the new card pool. Matched pair beats mismatched by ≥ 15 pp. |
| 12.6 | [12.6-cross-matchup-polish.md](12.6-cross-matchup-polish.md) | 12.5 | Tech-slot card edits to pull every cross-matchup into `[25%, 75%]`. Closes the step-12 arc. |

Steps 3 / 4 / 5 are parallelisable after 1 + 2. Step 6 is the largest;
it can begin once 1 and 2 land. Step 7 reshapes the interpreter as a
generator (7f) — that sub-commit was the long pole of the playtest
arc. Step 8 extends the engine (Mulligan → turn rotation → card play
→ target → Clash → SBA → victory) and adds the matching click-driven
UI; its long pole is 8e (Clash) since the encoding there is dense.

## Work-style conventions

- **One commit per step.** Branch off `main`, do the step, open a PR if
  the user wants review, merge, move on. No mega-PRs.
- **Tests before UI-only polish.** Every new endpoint gets an integration
  test in `tests/Ccgnf.Rest.Tests/`.
- **CI stays dotnet-only.** Node builds are a separate concern; the Vite
  output is checked in when it materially changes (the bundled site is
  small), or gitignored with a per-release `make web-build` run. See
  `web/overview.md` for the split.
- **Threshold-crosser updates README.** Any time a Status-table row
  changes state (e.g., "Web app" → "Scaffolded" → "Working v1"), update
  README in the same commit.

## Before starting any step

1. Read [../00-overview.md](../00-overview.md) and
   [../01-architecture.md](../01-architecture.md) if you haven't recently.
2. Read the step's file and the docs it references.
3. Check the devlog for the last entry — you may be picking up from a
   partial commit.

## Done-definition for each step

- All referenced code compiles and tests pass (`make DOTNET=dotnet.exe ci`
  on Windows, `make ci` on Linux).
- Any new endpoint has at least one integration test.
- Any new UI page is reachable from the nav and loads without console
  errors.
- README and `docs/plan/audit/current.md` are in sync with reality.
- A devlog entry describes what changed.
