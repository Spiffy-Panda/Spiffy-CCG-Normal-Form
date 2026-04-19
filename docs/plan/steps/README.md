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

Steps 3 / 4 / 5 are parallelisable after 1 + 2. Step 6 is the largest;
it can begin once 1 and 2 land. Step 7 reshapes the interpreter as a
generator (7f) — that sub-commit is the long pole of the playtest arc.

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
