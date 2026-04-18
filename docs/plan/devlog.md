# Dev log

Newest first. One entry per meaningful work session. Keep each entry to
≤ 200 words. Link to commits and files where relevant.

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
