# Step 7 — Playtest MVP

Turn `#/play/tabletop/{id}` from a JSON observer into a no-frills but
functional two-seat (human + CPU) playtest surface: cards-on-a-board
rendering, preset decks, deck selection, side inspector, state export.

Read before starting:
[../web/pages.md](../web/pages.md) §Tabletop,
[../web/rooms-protocol.md](../web/rooms-protocol.md),
[../audit/current.md](../audit/current.md),
[../reference/interpreter.md](../reference/interpreter.md).

## Resolved open questions

- **Preset decks live at `encoding/decks/`** — machine-adjacent, ships
  with the catalog.
- **Humanizer uses a per-builtin template table** — simple, fast to
  iterate, easy to diff; reflection over `Builtins` is rejected.
- **Interpreter re-entrancy is generator-shaped** — `Interpreter.Run`
  yields at each "input needed" point instead of replaying accumulated
  inputs from scratch. This is the unlock for CPU turns and mid-game
  action processing.

## Deliverables

1. AST humanizer library surface (`src/Ccgnf/Rendering/AstHumanizer.cs`)
   consumed by `CardMapper.ToDto()` and the cards-browser detail panel.
2. Preset deck catalog under `encoding/decks/` + `GET /api/decks/presets`.
3. `POST /api/rooms/{id}/join` accepts a `deck` field (preset id or
   explicit card list).
4. Low-fi `card` and `board` shared components; tabletop replaces its
   JSON dump with them.
5. Side inspector (humanized rules / raw `.ccgnf` source toggle).
6. `GET /api/rooms/{id}/export` + tabletop "Export" button.
7. CPU seat with a first-legal-action policy, **gated on** interpreter
   generator + `GetLegalActions` additions (sub-step 7f).

## Dependency order

```
A. AST humanizer ─┬─> E. Card/board rendering ─┐
                  └─> F. Side inspector         ├─> tabletop replaces JSON
B. Preset decks  ───> C. Deck → room join  ────┤
                                                ├─> D. CPU seat (needs 7f)
                                                └─> G. State export
```

## Sub-commits

### 7a. AST humanizer + cards browser integration

- `src/Ccgnf/Rendering/AstHumanizer.cs` with a single entry
  `string Humanize(AstNode)` and a `BuiltinTemplates` table
  (`Draw(n)` → `"Draw {n}"`, `Damage(tgt,n)` → `"Deal {n} damage to {tgt}"`,
  …). Unknown nodes fall back to `⟪<raw-render>⟫` so gaps are visible
  in the UI, not silently dropped.
- Extend `CardMapper.ToDto()` with `abilitiesText: string[]` alongside
  the existing `text` fluff field.
- Cards page detail panel renders `abilitiesText` above the source block.
- Tests: `tests/Ccgnf.Rendering.Tests/AstHumanizerGolden.cs` — one golden
  snapshot per faction file under `encoding/cards/`. Regression is a diff.

### 7b. Preset deck catalog

- `encoding/decks/*.deck.json`. Four starters — mono-EMBER aggro,
  mono-BULWARK control, TIDE+THORN combo, HOLLOW disruption. 30 cards each,
  legal in Constructed.
- Shape: `{ id, name, format, factions, cards: [{ name, count }] }`.
- `ProjectCatalog` indexes the directory at host startup; each entry is
  validated against `/api/cards` names.
- `GET /api/decks/presets` returns the catalog.
- Deck editor: "Save to library" button writes to `localStorage`; saved
  decks appear next to presets in every deck picker.

### 7c. Deck → room join

- Extend `POST /api/rooms/{id}/join` body:
  `{ name?, deck: { preset: "<id>" } | { cards: [{name, count}] } }`.
- `Room` stores a per-seat `DeckSpec`. At start, the interpreter's input
  assembly concatenates deck card names into the existing `inputs`
  pipeline (path of least resistance — revisit when 7f lands).
- Lobby create-room form + join form both get a deck dropdown sourced
  from presets + localStorage saves. Tabletop header shows each seat's
  deck name.
- Tests: join with unknown preset → 400; join with preset → seat has
  `DeckSpec` wired; state at step 0 contains that deck's cards.

### 7d. Low-fi card + board

- `web/src/shared/card.ts` — 63×88 div, faction-colored top stripe,
  name, cost pip, type, humanized abilities (1–2 lines, ellipsis).
  Hover → 2× zoom. No art.
- `web/src/shared/board.ts` — fixed CSS grid. Three Arenas across the
  middle, Resonance Field strip, Hand/Arsenal/Cache anchored per seat.
- `pages/play/tabletop.ts` replaces the JSON zone dump with `board`.
  Event log sidebar stays. When the engine halts, banner reads
  **"Engine stopped — last step N. Export to share."**
- No drag yet. Click interactions wired in 7f.

### 7e. Side inspector + state export

- `web/src/shared/card-inspector.ts`. Opens on right edge when a card is
  clicked in board, hand, graveyard, or cards browser. Tabs:
  **Rules** (humanized) / **Source** (`.ccgnf` block + `sourcePath:line`).
  Keyboard `R` / `S` to toggle.
- `GET /api/rooms/{id}/export` → `{ state, astFiles, seed, actionHistory,
  step }`. Tabletop toolbar: "Export" saves `room-{id}-step-{n}.json`.
  Re-importable via `POST /api/run` with the captured inputs for
  deterministic replay. Tests: export after N actions round-trips to
  identical `stateCount + gameOver`.

### 7f. Interpreter generator + legal-actions

Gate for 7g. **Largest code change in the step.**

- Reshape `Interpreter.Run` into a state machine that suspends on
  "input needed" points and resumes on new input. Preferred API:
  `InterpreterRun StartRun(...)` returning a handle with
  `WaitPending() → PendingInput` / `Submit(input)` / `TryExport()`.
  The existing synchronous `Run` becomes a thin wrapper that drives the
  generator with a pre-sequenced input list.
- Add `IReadOnlyList<LegalAction> GetLegalActions(int playerId)` on the
  run handle, computed from the current pending-input context.
- Rooms: replace the one-shot `InterpreterRt.Run` call in `Room.StartLocked()`
  with the generator. Action submissions drive the generator forward on
  the per-room lock. SSE frames fire on each yielded event.
- Tests:
  - Pre-sequenced inputs still produce the same state as pre-7f
    (determinism regression test).
  - Async drive: submit actions one at a time → state advances → same
    final state as all-at-once.
  - `GetLegalActions` returns `[pass]` at known pass-only points.

### 7g. CPU seat

- `SeatKind = Human | Cpu` on `Room`. Create-room form: "Add CPU"
  toggle + CPU deck picker.
- When a CPU seat needs to act, the room runs a decision loop:
  `GetLegalActions(cpuId)` → pick index 0 → `Submit`. "Performs
  actions if possible" baseline; pluggable behind `IRoomBot` for later
  heuristics.
- CPU seeded from `room.seed + playerId` for reproducibility.
- UI: tabletop shows a "🤖 thinking…" chip during CPU turns; disables the
  human's action buttons.
- Tests: human-vs-CPU game reaches the same halted state from two
  different action orderings with the same seed + decks.

### 7h. Hand-click wiring (polish, still in this step)

Now that the board and generator both exist:

- Click card in hand → board highlights `GetLegalActions` targets.
- Click target → `POST .../actions` with the resolved action.
- Click card anywhere (board/hand/graveyard) → opens inspector.
- Escape → closes inspector.

## Configuration

No new env vars. Existing `CCGNF_ROOM_TTL_SECONDS` / `CCGNF_ROOM_MAX`
still apply.

## README status update

- Web-app row → "Playtest v1" when 7a–7e + 7h land (human-vs-human
  playable through whatever the engine supports).
- Engine row threshold crossing depends on 7f's scope — if the generator
  lets the interpreter progress past Round-1 Rise for the first time,
  update the engine status row too.
- Test count updates at each sub-commit.

## Commit message templates

```
7a: Add AST humanizer with per-builtin template table + cards-browser abilities
7b: Add preset deck catalog under encoding/decks/ + /api/decks/presets
7c: Accept deck spec on room join (preset id or explicit card list)
7d: Render tabletop as low-fi card + board components
7e: Add card-inspector side panel and room state export
7f: Reshape Interpreter.Run as a generator with GetLegalActions
7g: Add CPU seat with first-legal-action policy (gated on 7f)
7h: Wire hand-click → legal-target highlighting → action submit
```

## Done when

- Human can create a room, pick a preset deck, add a CPU with a preset
  deck, and play through the engine's supported scope with cards
  rendered as cards (not JSON).
- Clicking any card opens the inspector; toggling shows humanized rules
  and raw `.ccgnf` source with a valid `sourcePath:line`.
- Export button produces a file that re-runs deterministically via
  `POST /api/run`.
- Cards browser shows humanized abilities text above the source block.
- All seven golden AST-humanizer snapshots pass.
- Devlog entries for each sub-commit; README status updated on threshold
  crossing.
