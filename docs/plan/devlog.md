# Dev log

Newest first. One entry per meaningful work session. Keep each entry to
≤ 200 words. Link to commits and files where relevant.

## 2026-04-18 — Step 3: Cards + Rules page

Shipped `#/cards` — the first real feature page past the playground.
Two-tab shell (Cards | Rules). Cards tab: 5 facet groups (faction,
type, cost bucket, rarity, keyword substring), faceted list with
AND-across-facets / OR-within, clickable rows → detail pane with
chips, flavor text, and the raw `Card Foo { … }` block extracted by
brace matching from `/api/project/file`. URL deep-linking works —
`#/cards?tab=rules&card=Spark` survives reloads.

Rules tab renders the declaration tree from `/api/project`: Entities
(5), Cards (116) grouped by faction, Tokens, Augmentations, Macros
(54). Each leaf lazy-loads its raw source on expand, with a regex
fallback for entities/tokens whose line offset wasn't pre-indexed.

Router now parses hash query strings into `URLSearchParams`. Added
shared `chip()` component and CardDto/ProjectDto/DistributionDto
client types.

No backend changes. 118/118 tests green. Verified Cards + Rules +
Interpreter in the preview; no console errors.

Next: Step 4 — Decks page.

---

## 2026-04-18 — Step 2: Cards + Project endpoints

Added the read-only data plane: `GET /api/cards`,
`POST /api/cards/distribution`, `GET /api/project`,
`GET /api/project/file?path=…`. Backed by a new
[ProjectCatalog](../../src/Ccgnf.Rest/Services/ProjectCatalog.cs) singleton
that loads every `*.ccgnf` from `encoding/` (override with
`CCGNF_PROJECT_ROOT`) lazily on first request, with an optional
`?reload=1` flag for forced refresh.

Wrinkle: the preprocessor concatenates source files into one expanded
string before handing it to the parser, so `AstDeclaration.Span.File`
collapses to `<project>` for every declaration. Recovered per-card /
entity / token source paths by regex-scanning the raw content in
`ProjectCatalog`; spans are still useful for line offsets within the
parsed tree.

Library surface: `PreprocessorResult` and `ProjectLoadResult` now carry
`MacroNames`. New DTOs: `CardDto`, `DistributionDto`, `ProjectDto`.
New mapper: `CardMapper`.

Tests: 9 new integration tests (`CardsEndpointsTests`,
`ProjectEndpointsTests`). 118/118 green.

Card-count assertions use ≥100 to match the current corpus (116); the
doc's 250 target is aspirational.

Next: Step 3 — Cards + Rules page.

---

## 2026-04-18 — Step 1: Vite scaffold

Stood up `web/` as a Vite + TypeScript project and migrated the existing
single-file playground under `#/interpreter` with behaviour parity (same
source textarea, seed/inputs controls, health pill, all eight action
buttons). New modules: [web/src/main.ts](../../web/src/main.ts),
[router.ts](../../web/src/router.ts),
[api/client.ts](../../web/src/api/client.ts) + `dtos.ts`,
[shared/nav.ts](../../web/src/shared/nav.ts) + `layout.css`,
[pages/interpreter/index.ts](../../web/src/pages/interpreter/index.ts) +
`style.css`.

Makefile gained `web`, `web-dev`, `web-build`. The build writes directly
to `src/Ccgnf.Rest/wwwroot/` via `vite build --outDir … --emptyOutDir`,
replacing the committed single-file playground. Root `.gitignore` now
excludes `web/node_modules/` and `web/dist/`. CI stays dotnet-only.

`dotnet test` (99 tests) passes, including
`EndpointsTests.Root_ReturnsPlaygroundHtml` — the title tag is preserved.
Preview server served the new build; Health and Run buttons both returned
200 with `status-ok`.

README status table gained a "Web app (`web/`)" row at "Scaffolded".

Next: Step 2 — `/api/cards` and `/api/project` endpoints. See
[steps/02-cards-project-endpoints.md](steps/02-cards-project-endpoints.md).

---

## 2026-04-18 — Planning docs landed

Wrote the `docs/plan/` tree: INDEX router, architecture + overview,
REST and C# library API docs, per-module reference digests
(`reference/ccgnf-lib.md`, `reference/interpreter.md`,
`reference/builtins.md`, `reference/ast-nodes.md`, `reference/rest.md`),
audit + migration notes, and per-step plans for the web-app arc.

CLAUDE.md now points to [INDEX.md](INDEX.md).

Status table entry for the web app stays "Not started" — this is plan
paperwork; no code changes to `src/` or `web/` yet.

Next: Step 1 (Vite scaffold). See
[steps/01-vite-scaffold.md](steps/01-vite-scaffold.md).

---

## Template for future entries

```
## YYYY-MM-DD — short title

What was done (2–4 sentences): which files, which commit hash(es),
which tests.

Why (1–2 sentences): link back to the step or audit item.

What's next: the next step in the arc, or any new item uncovered.
```

## Entries older than the above

*(none yet)*
