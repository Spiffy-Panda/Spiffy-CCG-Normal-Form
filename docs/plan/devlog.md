# Dev log

Newest first. One entry per meaningful work session. Keep each entry to
≤ 200 words. Link to commits and files where relevant.

## 2026-04-19 — 8f + 8g: Conduit collapse SBA + first victory condition

The interpreter's event loop has had a `RunSbaPass` hook since v1; it was
a no-op. Now it does the two SBAs that actually matter today:

- **Conduit collapse.** After each event dispatch, walks all Conduit
  entities; any with `integrity ≤ 0` and no `collapsed` tag gets marked
  collapsed (tag + `collapsed: true` characteristic) and an
  `Event.ConduitCollapsed(conduit, owner)` is enqueued.
- **Two-conduits-lost victory (GameRules §7.5).** Same pass — for each
  Player, counts their collapsed Conduits. At 2+ the player is tagged
  `lost` and `Event.GameEnd(loser, winner, reason: TwoConduitsLost)`
  enters the queue. Main loop flips `GameOver` on `GameEnd` as before.

Both rules are declared as Static abilities with `check_at: continuously`
in `encoding/engine/07-sba.ccgnf`. The full Static-ability evaluator
isn't wired yet, so this is engine-hardcoded to match the encoding's
intent — when the Static path lands, `RunSbaPass` becomes a driver that
walks those abilities instead.

Minor supporting change: `StateBuilder.PopulateBody` now hoists an
`owner:` body field to `Entity.OwnerId` when it evaluates to an entity
ref. Lets conduit fixtures using a `for owner ∈ {…}` clause carry
ownership without InstantiateEntity. Existing InstantiateEntity path
still sets OwnerId from the `owner` parameter.

New fixture (`conduit-collapse.ccgnf`) has four Conduits (two per
player, via InstantiateEntity) and a DoubleAnnihilate Maneuver that
picks two targets and deals 7 each. Four tests: owner hoist works,
ConduitCollapsed fires, GameEnd fires with loser/winner/reason, and
single collapses across both players do NOT end the game.

167/167 tests (+4). The engine can now end a game via combat (well —
via Maneuver damage; Unit/Clash damage lands in 8e).

---

## 2026-04-18 — 8a, 8c, 8d: turns roll; cards resolve; targets pick

Three slices of step 8 in one session.

**8a — Turn rotation + Mulligan.** Each phase handler chains
`BeginPhase(next, player: p)` at the end of its Sequence; the turn
walks Rise → Channel → Clash → Fall → Pass → (other player) → … Round
counter increments when control returns to first player. Engine
treats `Event.Lose` as terminal alongside `GameEnd`. Mulligan's
`Game.max_mulligans` reference was silently unbound — added to Game's
characteristics (per-Player copy stays, informational). `PerformMulligan`
stubbed to a single `SetCharacteristic(p, mulliganed, true)` call —
the real Target-backed implementation lands when the full mulligan
protocol follows up. `BeginPhase` became a real builtin emitting
`Event.PhaseBegin`. `InterpreterOptions.ShouldHalt` added (pre-dispatch
predicate) so tests can freeze state at a phase boundary.

**8c — Channel opens a priority window.** `EnterMainPhase(p)` now
enumerates affordable cards in hand and publishes an `InputRequest`
whose `LegalActions` include `[pass] ∪ {play:<entityId>, …}`.
Submitting `play:<id>` pays aether, moves card Hand → Cache, evaluates
`OnResolve` with `self` = card / `controller` = player, and emits a
`CardPlayed` event. Single-shot per turn (multi-card priority loop
lands with Interrupts). `GameState.CardDecls` indexes `AstCardDecl`s
by name for O(1) lookup at play time. Fixture + 5 tests covering
legal-action filtering, cost payment, effect resolution, zone move,
event emission.

**8d — Target(selector, chooser, bind).** Full implementation, not a
stub. Evaluates a 1-arg lambda selector against all entities,
publishes an `InputRequest` with `Kind="target_entity"` and one
`LegalAction` per matching entity (Label `"target:<id>"`, Metadata
carries kind + displayName). Consumer's submission binds the picked
entity to `target` (or caller's `bind:` override) in the effect's
environment. `DealDamage(target, amount)` drains
`current_ramparts → current_hp → integrity` in order and emits
`DamageDealt`. `Evaluator.LookupMember` grew intrinsic entity
accessors: `kind`, `id`, `displayName`, `owner`, `controller`.
Fixture has two Conduit entities via a `for`-clause (must share Kind
via decl.Name — entity Kind comes from declaration name, not the
body `kind:` field) and a `TargetedBlast` Maneuver that deals 2 to a
chosen Conduit. 3 tests cover publish, apply, and unknown-choice
no-op.

163/163 tests green (from 160 after the CSS commit; +3 for 8d). No
frontend changes in this batch — 8h/8i wire the new LegalAction
kinds into the tabletop UI.

Follow-ups I left:

- Existing full-game tests now provide 200-pass pads because Channel
  blocks per turn. When Interrupts / priority-window loops land,
  revisit the pad counts.
- `Lose → GameEnd` conversion still missing; Lose is terminal directly.
  8g wires a proper winner/loser GameEnd when victory conditions land.

---

## 2026-04-18 — Post-7h: deck names, inspector layout, step 8 plan

Two follow-ups after 7h landed:

- `Interpreter.SeedDecks` now accepts `InterpreterOptions.InitialDecks`
  (positional per-player list of card names). `Room.StartLocked` fills
  it from each `RoomPlayer.DeckCardNames`, so Arsenal cards carry
  their real name as `DisplayName`. The card inspector resolves
  against the catalog and shows real rules text for hand cards (e.g.
  "Refract — Maneuver · 1⚡ · C · TIDE — Resolves: target Unit: move
  target to target.owner.Hand") with the encoding source path.
- Tabletop right column is now a flex stack: inspector on top, event
  log on bottom, no overlap. Inspector mounts via
  `openInspector(card, rightColEl)` and is re-attached across
  tabletop re-renders so SSE updates don't blow away its state.

Also wrote [`steps/08-full-game.md`](steps/08-full-game.md) — the
roadmap from "engine halts at Round-1 Rise" to "1 human + 1 CPU game
reaches GameEnd via click events". Sub-commits 8a–8k cover Mulligan
firing, turn rotation, card play with cost, target resolution, Clash
damage, Conduit collapse SBA, one victory condition, frontend
turn/action/clash UI, and a Playwright end-to-end. Exit criteria +
risks called out; CPU stays first-legal throughout.

---

## 2026-04-18 — 7g, 7h: CPU seat + hand-click wiring

**7g — CPU seat.** `SeatKind` on `RoomPlayer`; `Room` pre-fills CPU seats
passed via the constructor. Driver detects a pending whose `PlayerId`
maps (by Players-list position) to a Cpu seat and auto-submits
`LegalActions[0]`, falling back to `pass`. CPU moves stream as
`CpuAction` SSE frames. Endpoint: `POST /api/rooms` accepts
`cpuSeats: [{ name?, deck }]`; deck resolution factored out so Create
and Join share the same preset lookup. Lobby grew a "+ Add CPU player"
UI (name + deck picker); tabletop roster marks Cpu seats with 🤖.
Seeded-RNG bot is deferred — "first legal" is deterministic without it.
+2 tests (auto-fill + unknown preset 400).

**7h — Hand-click → inspector + action bar.** New shared
`card-inspector.ts` (right-edge panel, Escape to close) usable from any
page; the tabletop wires it onto hand cards via `board.onCardClick`. A
new `play-action-bar` renders the labels from the last `InputPending`
SSE frame as clickable buttons when the viewer is the chooser, greyed
out otherwise. Inspector falls back to a raw entity dump when the
entity's `displayName` doesn't resolve to a `CardDto` (current v1 deck
seeding produces placeholder names like `Deck_Player1_4`).

Verified end-to-end in the preview: human + CPU, joined with
presets, state reaches `RoomFinished`, clicking a hand card opens the
inspector, Escape closes.

Follow-ups I noticed but didn't fix:
- The engine-banner text still reads "later phases arrive with 7f" even
  though 7f has landed — cosmetic.
- `viewerPlayerId` in `board.ts` is a roster PlayerId but
  `PlayView.players[].id` is an entity id, so the "bottom seat = me"
  check never matches. Pre-existing bug; both seats render the same
  hand today. Worth a tiny follow-up.

Test count: 150/150 (from 148 after 7f, +2 for CPU).

---

## 2026-04-18 — 7f: Interpreter generator + legal-actions

Reshaped `Interpreter.Run` into a cooperative generator. The sync entry is
now a thin wrapper that drives `StartRun` → `InterpreterRun` with a
pre-sequenced input list; pre-7f behavior is preserved bit-for-bit (the
existing determinism test still passes against the serialized state).

Implementation is "thread per run" — `BlockingInputChannel : IHostInputQueue`
bridges the synchronous `Choice → Next(request)` call on the interpreter
thread to the consumer's `WaitPending` / `Submit` on another thread. Chose
this over CPS-converting every `Evaluator` / `Builtins` method: blast
radius was hundreds of call sites and didn't fit a single sub-commit. Room
limit is <10 concurrent, so a few extra thread-pool threads is fine.

New public surface: `InputRequest`, `LegalAction`, `RunStatus`,
`InterpreterRun`, `InterpreterOptions.OnEvent`. `Builtins.Choice` now
evaluates its chooser to a PlayerId and publishes option keys as
`LegalAction`s before blocking. `Room.StartLocked` drives the handle on a
background task; `AppendAction` pushes into a `BlockingCollection` that
the driver drains.

Caught one race during test-driving: the consumer's `WaitPending` could
observe the previous `_current` if the interpreter hadn't cleared it yet
after reading the response. Fixed by clearing `_current` + resetting the
request signal atomically inside `Submit`.

148 tests pass (143 → 148, +5 in `InterpreterRunTests.cs`). Docs updated:
[reference/interpreter.md](reference/interpreter.md).

Deferred: the Resonance encoding's `Game.max_mulligans` is unbound on
Game (it's declared on Player), so MulliganPhase collapses to `Repeat(0, …)`
and no live Choice fires. Added a fixture
[`tests/Ccgnf.Tests/fixtures/choice-on-start.ccgnf`](../../tests/Ccgnf.Tests/fixtures/choice-on-start.ccgnf)
to exercise Choice directly — encoding fix is its own change.

---

## 2026-04-18 — Steps 4, 5, 6 landed

Shipped the remaining web-app steps in one batch:

- **Step 4 (Decks).** `POST /api/decks/mock-pool` samples N cards from
  the catalog, weighted by target rarity (44/32/18/6). Frontend `#/decks`
  ships a 3-column layout: format selector (Constructed / Draft),
  searchable pool, deck list with max-copies guard, live distribution
  bars, and localStorage persistence keyed by `deck:<format>:<name>`.
  5 new integration tests.

- **Step 5 (Raw).** `#/raw` file tree with a regex syntax highlighter
  (comments / strings / numbers / keywords / operators). Reload button
  hits `/api/project?reload=1`. Added a test asserting `loadedAt`
  advances after a reload. Endpoint parsers now accept `reload=1/true/yes`
  — ASP.NET Core's `bool` binder rejects `"1"`.

- **Step 6 (Rooms).** Backend: `LiveInputQueue`, `Room`, `RoomStore`,
  `SseBroadcaster`, `RoomTtlSweeper` HostedService, full `/api/rooms`
  endpoint surface. Frontend: `#/play/lobby` (create + list) and
  `#/play/tabletop/{id}` (state snapshot + SSE event log + pass action).
  v1 runs the interpreter synchronously on start — actions buffer for a
  future async refactor (6c). 6 new tests.

130/130 green. No console errors in preview.

---

## 2026-04-18 — Rules tree: Augment source extraction

Follow-up to Step 3. Expanding an Augmentation in the rules tree 404'd
because `ProjectCatalog` only indexed Card/Entity/Token declarations by
name, and augments like `Game.abilities += Triggered(…)` recur across
files — name-keyed lookup collapses them.

Replaced the per-kind name → `(path, line)` map with a single
`FileDeclarations` table (`path → List<FileDeclaration{Kind,Name,Label,Line}>`)
built from a regex scan that now covers `Card`, `Entity`, `Token`, and
`Augment` (`target.path +=` / `target.path = …` with at least one `.` or
`[…]` in the target). `CardLocations` etc. stay as name-keyed views for
the card list. `/api/project`'s `byFile` shape changes from
`Record<path, string[]>` to `Record<path, {label, line}[]>` so the rules
tree can jump straight to each declaration's source. `extractSourceBlock`
on the client now counts `(){}[]` together so augment expressions
terminate at their matching `)` instead of bleeding into the next
declaration.

118/118 tests still green. Augment expansion shows the right block.

---

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
