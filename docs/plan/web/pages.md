# Web — pages

Detail per page. Each section lists what the user sees, what APIs it
consumes, and what shared components it leans on.

## `#/cards` — Cards + Rules browser

**Layout:** two-column. Left: faceted card list. Right: selected card
detail or the Rules tab.

**Facets:**
- faction (EMBER / BULWARK / TIDE / THORN / HOLLOW / NEUTRAL)
- type (Unit / Maneuver / Standard / …)
- cost (≤1, 2, 3, 4, 5, 6+)
- rarity (C / U / R / M)
- keyword (free-text match across card.keywords)

**Endpoints:** `GET /api/cards`.

**Detail panel:** name + factions + type + cost + rarity + keywords +
rules text + a raw `.ccgnf` block (the card's source; pretty-printed
with the `Raw` page's highlighter).

**Rules tab:** NOT a markdown render. Shows a tree:

```
Entities (4)
  ├─ Game
  │   └─ Triggered × 6     [on: Event.GameStart] [on: Event.PhaseBegin(Rise,…)] …
  ├─ Arena                  for pos ∈ {Left, Center, Right}
  ├─ Player                 for i ∈ {1, 2}
  └─ Conduit[owner, arena]  template
Cards (N)
Tokens (M)
Augmentations (K)
```

Each node is expandable; expanded leaves show the raw `.ccgnf`
expression verbatim with a "will parse into human rules later"
placeholder line. Driven by `GET /api/project`.

## `#/decks` — Deck construction

**Layout:** three columns. Left: format selector + filters. Middle:
card pool (scrollable grid). Right: current deck with counts +
distribution panel.

**Format selector:** pulls format names from a small server-side table
(for v1, hard-coded: Constructed, Draft). Switches:
- **Constructed** — card pool is the full `/api/cards` list.
- **Draft** — card pool is served by `POST /api/decks/mock-pool`
  which returns a synthesized 40-card pool seeded deterministically.
  The UI labels this "Mock draft pool — real drafting pending."

**Deck list:** card name + count spinner (`+` / `−`). Max copies per
format enforced client-side; format rules come from a shared constants
module mirrored from `grammar/GrammarSpec.md` / `design/GameRules.md`
§Formats.

**Distribution panel:** live-updated pie/bars for faction mix, cost
curve, rarity split. Uses `GET /api/cards/distribution?cards=…`. No
chart library — CSS `width: N%` bars suffice.

**Persistence:** `localStorage` keyed by `"deck:<format>:<name>"`.
Save/load dialog for named slots. Export as `.ccgnf`-ish list later.

## `#/interpreter` — Playground

Successor to the current `src/Ccgnf.Rest/wwwroot/index.html`. Same
functionality, migrated into the Vite app with visible improvements:

- Multi-file editor: tab per loaded file, "Load from encoding/" button
  that pre-fills tabs from `/api/project`.
- Seed + inputs fields (unchanged).
- Stage buttons: Preprocess, Parse, AST, Validate, Run.
- Output: two sub-tabs — **State tree** (zone-by-zone rendering of
  `GameStateDto`) and **JSON** (raw response).
- Status pill: last-stage `ok/error`, diagnostics count.

**Endpoints:** `/api/preprocess`, `/api/parse`, `/api/ast`,
`/api/validate`, `/api/run`, `/api/project`.

## `#/play/lobby` — Rooms lobby

**Layout:** top: "Create room" form (project picker + seed). Below:
"Open rooms" table: id, host, player slots (1/2 vs 2/2), created-at,
step count, Join button.

**Endpoints:** `GET /api/rooms`, `POST /api/rooms`,
`POST /api/rooms/{id}/join`. On successful join, stores `{playerId,
token}` in `sessionStorage`, navigates to `#/play/tabletop/{id}`.

See [rooms-protocol.md](rooms-protocol.md) for full detail.

## `#/play/tabletop/{roomId}` — Active tabletop

**Layout:** center-of-screen board showing the three Arenas stacked, the
Resonance Field strip below, and each player's Hand / Arsenal / Cache
anchored at their side of the screen. Top bar: turn phase, aether,
active-player indicator. Right sidebar: event log (SSE feed).

**Connections:**
- `EventSource('/api/rooms/{id}/events')` — state-change stream.
- `GET /api/rooms/{id}/state` once on load; subsequent updates merge
  from SSE.
- `POST /api/rooms/{id}/actions` when the user submits an action.

**Actions in v1 scope:** `pass` (mulligan pass) and eventually (once the
engine supports it) card plays, attack declarations, and target picks.
Until then, the Tabletop can receive `Run` outputs and render the
final state — functioning as a rich viewer.

## `#/raw` — Loaded project

**Layout:** left: file tree. Right: file content with basic syntax
highlighting.

**Endpoints:** `GET /api/project` for the tree; the file content is
inlined in the `/api/project` response or fetched lazily via a second
endpoint `GET /api/project/file?path=…`.

**Highlighting:** keyword-set + string/number/comment regex pass, no
PEG parser. Enough to read, not to edit.

Also shows:
- Collected macros (from the preprocessor).
- Declaration counts.
- Diagnostics if any (rare — the REST host only serves files it validated).

## Shared components

- `shared/nav.ts` — top bar: links to each route, health pill, port number.
- `shared/status-pill.ts` — ok / error chip used on every page.
- `shared/zone-tree.ts` — renders a `GameStateDto` as a nested zone
  view; used by Interpreter and Tabletop.
- `shared/diagnostics-list.ts` — standard rendering of `DiagnosticDto[]`.
