# Web-app verification checklist

Agent-facing runbook. Every checkbox below must pass on the preview
`ccgnf-rest` server after a fresh `make web-build`. Stop here if
anything fails; note the failure before moving on.

Tools used: `mcp__Claude_Preview__preview_start`, `preview_eval`,
`preview_click`, `preview_console_logs`, `preview_network`,
`preview_stop`. Do **not** use Claude-in-Chrome — the preview tools are
authoritative for this app.

## Preflight

1. **Rebuild the bundle.** `cd web && npm run build` (writes to
   `src/Ccgnf.Rest/wwwroot/`). The REST host serves the result as
   static files; a stale bundle silently hides regressions.
2. **Start the preview server.** `preview_start name=ccgnf-rest`.
   If already running, grab the `serverId` from `preview_list`.
3. **Record the baseline.** `preview_console_logs level=error` must
   be empty before you touch anything.

Tip: cards share names (`Entity Arena[pos]`, `Augment Game.abilities`)
across files — treat `path:line` as the unique key in verification.

## `#/interpreter` (step 1)

Load: `preview_eval` with `window.location.hash = '#/interpreter'`.

- [ ] Header shows `CCGNF Playground` and the port pill reads `19397`.
- [ ] Health pill reads `health: ok` within 1s of load.
- [ ] 8 action buttons present: Health, Preprocess, Parse, AST,
      Validate, Run, Create session, List sessions.
- [ ] Click **Health** (CSS `button[data-action=health]`): output
      contains `"service":"ccgnf.rest"`, status pill `status-ok`.
- [ ] Click **Run**: output is ≥10 KB, status-ok, and the body
      starts `{"ok":true,"state":{"stepCount":`.
- [ ] `preview_network filter=failed` still empty after each click.

## `#/cards` (steps 2 + 3)

Load: `window.location.hash = '#/cards'`.

- [ ] Nav link `Cards` has `.active`; other top-level links do not.
- [ ] `.card-list-header` reports ≥100 cards.
- [ ] 5 facet groups: Faction, Type, Cost, Rarity, Keyword. A
      `facet-clear` button exists **but is not a tab**
      (regression from step 3 — check `.cards-tab` count is 2).
- [ ] Check one Faction box (e.g. EMBER): list shrinks; every
      `.card-list-item` now has a matching `.chip-faction` label.
- [ ] Click the first `.card-list-item`: `.card-detail-title`,
      chips (faction / type / cost / rarity / keywords), flavor
      text (if any), and a `.card-detail-source` block render.
      URL hash updates to `#/cards?card=<Name>`.
- [ ] `.card-detail-source` contains `Card <Name> {` and terminates
      at the matching `}` — no bleed into the next card.

Switch to Rules tab (click the Rules `.cards-tab`).

- [ ] `.rules-group-summary` labels: Entities, Cards, Tokens,
      Augmentations, Macros, each followed by `(N)`.
- [ ] Cards group expands into faction subgroups; each card leaf
      has `path:line` in muted text.
- [ ] Expand an Entity: source block appears, terminates at its
      matching `}`. No `<project>:0` labels anywhere — if you see
      one, the `FileDeclarations` index is broken.
- [ ] Expand **two** different Augmentations with the same name
      (e.g. two `Augment Game.abilities` entries): each loads its
      own distinct source block, not a shared one. This is the
      Step 3 regression that motivated
      [`f06cd93`](https://github.com/Spiffy-Panda/Spiffy-CCG-Normal-Form/commit/f06cd93).
- [ ] Augment source ends at the matching `)` / `]` / `}` — the
      `extractSourceBlock` fix must count all three bracket kinds.

## `#/decks` (step 4)

Load: `window.location.hash = '#/decks'`.

- [ ] Format selector defaults to Constructed; pool shows all
      `/api/cards` entries.
- [ ] Click a pool row: deck total advances, distribution bars
      appear (4 groups: Faction / Type / Cost / Rarity).
- [ ] Max-copies guard: click `+` 5× on the same card — count
      caps at the format's `maxCopies` (4 for both v1 formats).
- [ ] Switch format to Draft: pool collapses to exactly 40 cards,
      `.decks-warning` reads "Mock draft pool — real drafting
      pending.", deck resets to empty.
- [ ] Reseed: change the seed input, click Reseed — new 40-card
      pool, different card names (with high probability; compare
      first 3).
- [ ] Save: click Save, provide a name — it should appear in the
      Load dropdown. Switch format away and back; the load
      dropdown should only show decks saved under the current
      format (localStorage key `deck:<format>:<name>`).
- [ ] `preview_network` shows exactly one `POST /api/decks/mock-pool`
      per format-switch-to-draft or reseed.

## `#/raw` (step 5)

Load: `window.location.hash = '#/raw'`.

- [ ] Sidebar: 22 `.raw-tree-file` rows across 3 `.raw-tree-dir`
      headings (`cards`, `common`, `engine`). Byte counts render.
- [ ] Summary panel lists counts per declaration kind and a
      non-empty macros list.
- [ ] Click an `encoding/cards/*.ccgnf` file: `.raw-viewer-header`
      shows `<path> — <N> chars`; body renders with `.hl-keyword`,
      `.hl-comment`, `.hl-string`, `.hl-number` spans present
      (spot-check: ≥20 keyword spans on a real card file).
- [ ] Click **Reload**: `.muted` timestamp under Summary advances.
      `preview_network` shows `GET /api/project?reload=1 → 200`.

## `#/play/lobby` (step 6)

Load: `window.location.hash = '#/play/lobby'`.

- [ ] Nav link `Play` is `.active` here AND on the tabletop page
      (top-segment match; regression signal if inactive on
      tabletop).
- [ ] Click "Create with loaded encoding". After a brief delay the
      hash changes to `#/play/tabletop/r_<12hex>`.
- [ ] Hit Refresh on the lobby: the new room appears in the table
      with state `WaitingForPlayers`, occupied `0/2`.

## `#/play/tabletop/{id}` (step 6)

On landing (after creating from the lobby):

- [ ] Header: room id, state `WaitingForPlayers`, `0/2` players.
- [ ] "Claim a seat" button is enabled; game-state block reads
      `(waiting for second player)`.
- [ ] Drive two joins via `fetch('/api/rooms/{id}/join', …)` from
      `preview_eval` (UI flow works too but the prompt dialog is
      fragile under `preview_click`). Expected: 200, token, second
      join returns playerId=2.
- [ ] After both joins, `GET /api/rooms/{id}` returns `Active`
      with `occupied=2`; `GET /api/rooms/{id}/state` returns a
      full `GameStateDto` with `playerIds.length == 2` and
      `arenaIds.length == 3`.
- [ ] Reload the page. SSE backlog replays: event log shows at
      least `RoomStarted` and 2× `PlayerJoined`. State block
      summarizes `stepCount`, `players`, `arenas`, `entities`.
- [ ] Join one more seat via `fetch` → expect 409 Conflict.
- [ ] Action with wrong token → `preview_network` shows 401 for
      that POST; with a real token → 202 and a new
      `ActionAccepted` event frame in the log.

## Cross-cutting

- [ ] `preview_console_logs level=error` is empty after touching
      every page. A single warning is OK; a single error is not.
- [ ] `preview_network filter=failed` is empty.
- [ ] Hit every route via a direct hash load (no navigation from
      another page). No flashes of unstyled content; no "No route
      for …" fallback unless you intentionally visit a bad hash.
- [ ] Open DevTools → Network and confirm the bundled JS + CSS
      files match the filenames listed by `npm run build`'s
      output. A mismatch means the REST host is serving a stale
      `wwwroot` — rebuild before re-verifying.

## Common failure signatures

| Symptom | Usual cause |
|---------|-------------|
| `<project>:0` in a Rules-tree label | `ProjectCatalog.IndexFileDeclarations` regressed; augment / entity scan no longer matches. |
| Augment source bleeds into the next line | `extractSourceBlock` only counts `{}` again; re-add `()` / `[]`. |
| `POST /api/decks/mock-pool` returns 400 on `format=draft` | Catalog loaded an empty project (check CWD resolution in `ProjectCatalog.Load`). |
| Lobby's Create button 201s but tabletop shows "no state" forever | `LiveInputQueue.Drain` not called before `Interpreter.Run`, or the synchronous run threw an exception caught silently. |
| `reload=1` is a 400 | Endpoint param typed as `bool` instead of `string`; ASP.NET Core's bool binder rejects `"1"`. |
| Nav link doesn't highlight on `/play/tabletop/*` | `topSegment` helper regressed; it must strip everything past the first `/…` segment. |

## Wrap up

Stop the preview server (`preview_stop`) before running `dotnet test`
— a running host holds `Ccgnf.dll` and blocks the rebuild.
