# Dev log

Newest first. One entry per meaningful work session. Keep each entry to
≤ 200 words. Link to commits and files where relevant.

## 2026-04-20 — Step 13 waves 2.5-5: the engine is keyword-complete

Finished the remaining step-13 arc in one long session. Six waves in:

- **2.5 Lambda-filter shorthand.** `AttachCardAbilities` expands
  `OnArenaEnter(filter: u -> p(u), effect: E)` and `OnCardPlayed(...)`
  to raw Triggered, inlining the filter body as an If-guard with the
  lambda's param name reused as the event binding. CohortCaptain's
  BULWARK Fortify grant aura and RippleBreaker's Reshape-on-play
  both fire end-to-end.
- **3 Replacement dispatch.** Replacement walker runs at the two
  points where it matters: arena collapse → `DestroyUnit` (Recur
  redirects to Arsenal-bottom), and PlaceUnit post-stats (Unique
  bounces duplicates to Cache). Four new builtins: `MoveTo`,
  `bottom_of`, `HasDuplicateInPlay`, `has_keyword`.
- **4 Phantom.** Keyword synthesises two Triggered abilities: a
  StartOfClash fade (sets `phantoming` tag, zeroes Clash
  contribution) and a PhaseEnd(Clash) return (MoveTo hand, +1
  base_cost_reduction counter, emits `Event.PhantomReturn`).
  BlankfaceCultist's raw PhantomReturn trigger now fires. v1
  auto-fade — Choice prompt can layer on later.
- **5 ResonanceField.** FIFO 5-slot list in each Player's
  Characteristics, pushed on every CardPlayed. `CountEcho`,
  `Resonance(F, N)`, `Peak(F)`, `Banner(F)`, `Tiers([(cond, eff),
  ...])`, `When(cond, eff)` builtins default to the env's
  `controller` binding.
- **Bonus.** `Heal` (additive inverse of DealDamage, caps at
  starting_integrity), `CreateToken` / `Sprawl` (THORN token
  generation, emits EnterPlay so Rally fires on them), `DriftMoveUnit`
  (adjacency-aware move), Shroud filter in `Target`, Surge cost
  rebate in `ComputeEffectiveCost`, Ignite upgraded from
  Conduit-chip approximation to opposing-Unit-with-≤2-Ramparts
  path with Conduit fall-through.
- **Pattern matcher** now resolves `self.arena` to the entity's
  arena parameter and `self.controller` to its OwnerId ref;
  `EnterPlay` events carry `arena` as a symbol so pattern
  comparisons work cleanly.

Bench movement (`post-wiring-full.*.results.json`) vs post-Ontrig:
- KeywordCoverage trigger-fires: 19,736 → **201,398**. Zero
  declared-but-silent cards in the cast log.
- PairCorrectly draw rate: 34.6 % → **0.0 %**. Every game resolves.
- EmbHell decisive WR: 34.6 % → 66.7 % (+32 pp; Ignite chips
  work, Resonance tiers gate EMBER copies from Eremis).
- AiDeckMatrix ember-aggro: 44.1 % → 68.2 % (+24 pp).
- AiDeckMatrix tide-thorn: 81.1 % → 66.4 % (Drift moves things,
  Recur reshuffles, Phantom fades deny clash contributions).
- Per-AI aggregates inside a 3-pp band — bots still play the game.

283+ Ccgnf/Bots/Rest tests green. Step 13 exit criteria all ticked
in docs/plan/steps/13-engine-completion.md. Residual gaps (Kindle
counter-fire, Reshape echo swap, Interrupt Debt) deferred as
small-scope, low-impact.

## 2026-04-20 — Step 13 wave 2: Triggered-on-Unit + Mend/Rally/Ignite live

Closed the major step-13 gaps the cast log pointed at. Landed:

- **Shorthand triggers expanded C#-side** in `AttachCardAbilities`
  (`OnEnter`, `OnPlayed`, `StartOfYourTurn`, `EndOfYourTurn`,
  `StartOfClash`, `EndOfClash`) — the ccgnf preprocessor never
  sees `engine/02-trigger-shorthands.ccgnf` when it processes
  `cards/` (alphabetical sort puts cards first); we no longer
  depend on that.
- **`Mend`, `Rally`, `Ignite` keyword synthesis** in
  `KeywordRuntime.ApplyKeywords` — the keyword list on a card
  now produces real Triggered abilities, with two helper builtins
  (`HealSelfArenaConduit`, `IgniteTickArenaConduit`) to paper over
  the missing `self.controller.Conduit(self.arena)` accessor.
- **Pattern matcher** resolves `self.controller` and `self.arena`
  against the owning entity; **`PhaseEnd(phase=Clash)`** is now
  emitted after the arena loop so EndOfClash triggers fire.
- Also fixed 2 cards (Eremis, Thessa) whose `OnCardPlayed` call
  passed 3 named args to a 2-arg macro — rewritten to use raw
  `Triggered(...)` with the extra guard preserved.

Bench movement (`post-wiring-ontrig.*.results.json`) vs post-Fortify:
EMBER aggro decWR +17.3 pp (Ignite chips); BULWARK control −12.1 pp.
Cast log now reports **19,736 trigger-fires** across a 992-game
KeywordCoverage run (was 0).

Still-silent cards (`OnArenaEnter` + `OnCardPlayed` with lambda
filters, `PhantomReturn`) listed in step 13 plan of work.

## 2026-04-20 — Step 13: Fortify + Sentinel wired; BulFort stall cell breaks

Opened [step 13](steps/13-engine-completion.md) to execute the
[engine completion guide](engine-completion-guide.md). Landed §2's
three-sub-step pass as three commits:

- **A.** `KeywordRuntime` helper reads a Card's `keywords:` list onto
  the runtime Unit; `PlaceUnit` calls `ApplyKeywords`; helpers for
  `GetClashProjectedForce` / `GetClashFortification` honour Sentinel.
- **B.** `EffectiveRamparts` gates Fortify(N) on the controller's
  Conduit at integrity ≥ 4, re-evaluated live (no counter mutation).
- **C.** `ResolveClashPhase` replaces the per-attacker Force loop
  with the per-Arena `incoming = max(0, projected − fortification)`
  formula; emits one `DamageDealt` per Arena carrying the math.

8 keyword probes land in
[`tests/Ccgnf.Tests/KeywordWiringTests.cs`](../../tests/Ccgnf.Tests/KeywordWiringTests.cs);
all 135 Ccgnf + 93 Bots + 53 REST tests pass.

Post-wiring bench (guide §2.3 prediction: ≥ 5 pp on BulFort-vs-EmbHell):
**+56.8 pp** on the bulwark-ctl × hellfire matrix cell (10.3 % →
67.1 %). PairCorrectly draws drop 37.1 → 21.7 %; BulFort decisive
WR 36.1 → 71.6 %. BULWARK is finally a wall; EMBER's raw Force no
longer torches through. Artifacts:
`ai-testing-data/post-wiring-fortify.{PairCorrectly,AiDeckMatrix}.results.json`.

Next: Triggered-on-Unit dispatch (guide §3.2 wave 2).

## 2026-04-20 — Step 12.2 + 12.3: engine audit + card threat audit

Ran both audits in one session; no engine or card edits yet.

**12.2 (engine).** Probed with a Hellfire + `ember-aggro` mirror — the
most damage-dense configuration the pool allows. Result: `2-1-117`
over 120 games, **97.5 % draw rate** at 428 avg steps. Even with
aggressive cards and aggressive weights on both sides, the 1:1
Force-minus-Fortification Clash formula absorbs almost all damage
in a symmetric matchup. Verdict: **rules need a knob**.

Queued knobs (none shipped). Knob 1 (Clash Incoming ×2) is the
primary candidate; Knob 3 (harness-only tiebreaker) can ship
independently when useful. 12.2's follow-up sequencing is explicitly
gated behind 12.3's first authoring pass — see the updated
[`12.2`](steps/12.2-engine-sanity-pass.md) doc.

Full writeup: [`docs/plan/balance/engine-audit-2026-04-20.md`](balance/engine-audit-2026-04-20.md).
Probe artifact: [`ai-testing-data/12.2-ember-speed.results.json`](../../ai-testing-data/12.2-ember-speed.results.json).

**12.3 (cards).** Tagged every one of 115 cards in
`encoding/cards/*.ccgnf` as closer / setup / disruption / filler.
Faction health: EMBER 83 %, BULWARK **0 %**, TIDE **0 %**, HOLLOW
**0 %**, THORN 14 %. Three of five mono-factions ship with literally
zero Conduit-damage cards. That's the dominant cause of the 80 %
baseline draw rate.

Updated [`12.3`](steps/12.3-card-threat-audit.md) with the full
authoring queue (19 closers across 4 factions with design hooks),
the `card-cluster` SKILL.md update plan (role-bucket awareness,
gap-aware default, role tags in DISTRIBUTION.md), and the 12.4
coordination contract. Full card-by-card audit at
[`docs/plan/balance/card-pool-2026-04-20.md`](balance/card-pool-2026-04-20.md).

No card files edited this session — authoring is the next action,
per-card-per-commit with bench attached. Engine knobs stay queued;
pull only if the card-authoring pass doesn't reach the ≤ 40 %
draw-rate target.

---

## 2026-04-20 — Step 12.1: AI floor fix (no promotion)

Audited all four experimental bots for
`conduit_softness ≥ threat_avoidance` under `pushing` + `lethal_check`.
Two violators: Fortress (`pushing` cs 0.6 vs ta 2.5) and Reaper
(`pushing` cs 0.8 vs ta 1.0). Pre-edit snapshots preserved under
`experimental/2026-04-19-{fortress,reaper}-prefloor/`; live weights
edited in place. Hellfire and Wavebreaker already passed, no edit.

Bench (40 games, matched deck, new vs old):

- Fortress on `bulwark-control`: 2-2-36, decisive WR 50 %. Mirror too
  draw-heavy for a confident signal — below 55 % promotion bar but
  not a regression.
- Reaper on `hollow-disruption`: 4-11-25, decisive WR **26.7 %** —
  regression. Pre-edit Reaper was accidentally winning its mirror by
  stalling; forcing a closer pulls it out of that equilibrium. Kept
  anyway because the floor rule is a hard invariant.

PairCorrectly re-run at baseline seed: draw rate **unchanged at
81.7 %** (byte-identical per-pair numbers vs 12.0). `pushing` intent
is rarely the active state in matched-pair cross-matchups. **Exit
criterion ≤ 40 % draw rate missed by 41.7 pp** → diagnosis routes to
Step 12.3 (card threat audit, primary) and Step 12.2 (engine sanity
pass, secondary). The closer-density deficit in bulwark-control +
hollow-disruption is the real blocker; weights alone can't fix it.

`encoding/ai/stable/` still absent — no bot cleared the 55 % bar this
cycle. Full writeup: [`docs/plan/balance/ai-floor-2026-04-20.md`](balance/ai-floor-2026-04-20.md).
Bench artifacts: [`ai-testing-data/12.1-*.results.json`](../../ai-testing-data/).

---

## 2026-04-20 — Step 12.0: balance baseline captured

Ran the two reference tournaments at post-bump defaults (40 games /
matchup, 6000 inputs, 150000 events) and checked the artifacts in:

- [`ai-testing-data/PairCorrectly.baseline.results.json`](../../ai-testing-data/PairCorrectly.baseline.results.json) + `.learning.jsonl`
- [`ai-testing-data/SimpleSweep.baseline.results.json`](../../ai-testing-data/SimpleSweep.baseline.results.json) + `.learning.jsonl`

Human summary: [`docs/plan/balance/baseline-2026-04-20.md`](balance/baseline-2026-04-20.md).

Key numbers: PairCorrectly drew 196/240 (81.7 %), SimpleSweep drew
659/840 (78.5 %). Average game length 374–393 steps. Matched-pair
leaderboard: Wave > Fort ≫ Hell > Reap. Mirror-deck leaderboard (on
`bulwark-control`): Wave ≈ Reap ≈ Fort > Hell > Util ≫ Fixed.

Exit via the "AI floor is bad" branch — both configs above the 70 %
draw threshold. Three most-lopsided matchups all implicate the
`reaper` + `hollow-disruption` pair; `fixed` piloting
`bulwark-control` draws 97 % in its mirror. Hypothesis: weak
closing pressure (intent `lethal_check`, consideration
`conduit_softness`), not deck-level imbalance. Step 12.1 will
iterate weights, not cards.

No code changes.

---

## 2026-04-19 — Step 10.2 complete (utility bot + BT shell + tournament)

Delivered 10.2a–10.2i in one arc. New `src/Ccgnf.Bots/` project holds
the extracted `IRoomBot` + `FixedLadderBot`, the `UtilityBot` with
weight tables and seven considerations (`on_curve`,
`tempo_per_aether`, `lowest_live_hp`, `opponent_priority`, `overlap`,
`conduit_softness`, `threat_avoidance`), a stateless phase-BT shell
loading from JSON, `PhaseMemory` for sticky intent, and a
`BotMatchRunner` + `TournamentRunner` harness in `Bench/`.

REST gains `/api/ai/{bots,weights,preview-score,tournament}`, all
gated behind `CCGNF_AI_EDITOR=1`. Room takes a new `botKind` knob
(`fixed` | `utility`) so operators can flip the default without
recompiling. Deck JSON schema adds `archetypes: string[]` +
`suggestedAi?: string`; ember-aggro and bulwark-control got tags.

Web ships a minimal `#/ai` route: bot list, weights textarea with
Save, deck-picker tournament runner. Preview-verified — page loads,
gated endpoint returns 404 with the expected graceful error banner.

269 tests pass (127 Ccgnf, 52 Rest, 90 Bots). The benchmark harness
runs real Resonance games end-to-end.

---

## 2026-04-19 — Units render on arenas + phase tracker

Two frontend-only follow-ups to make 8e visible.

- `board.ts` renderUnitLane now actually renders Units. Iterates
  `view.cardsById`, filters by `ownerId === player.id`,
  `characteristics.in_play === "true"`, and
  `parameters.arena === <arenaPos>`. Each match renders via
  `renderCardFromEntity` with click → inspector. Previously the
  comment said "no unit-in-arena data yet (interpreter stops before
  units enter play)" — stale since 8e.
- Stale "Engine state — round N … 8e–8g" banner replaced with a
  live phase tracker: round number, five phase pills (active one
  highlighted), active-seat chip ("Your turn" / "Opponent's turn"),
  and the SSE step count. Reads directly from `state.currentPhase`
  (set by `PhaseBegin` SSE frames) and `buildView(state.gameState)`.

Verified in preview: played Cinderling into Arena Left, it rendered
in the top-of-arena unit lane at the correct side; tracker updated
to "Round 1 · Rise Channel Clash Fall Pass · Your turn · step 7"
with Clash highlighted.

---

## 2026-04-19 — Battlefield zone + clash-UI humanizer

Closes the gap between 8e landing and a full game being playable in the
browser against the real Resonance encoding.

- Player entity in `encoding/engine/04-entities.ccgnf` now declares a
  `Battlefield` zone alongside Arsenal/Hand/Cache/ResonanceField/Void.
  Units played via the new 8e path land there. The real per-arena
  partitioning (§3.2's `Arena.units[Player]`) is still expressed through
  the Unit's `arena` parameter; child-zone nesting inside Arenas is a
  follow-up.
- Frontend humanizer gained cases for `target_arena` (renders "Arena
  Left") and `declare_attacker` (renders "Attack" / "Hold"). Everything
  else already looked right via 8i.

Verified end-to-end in preview with EMBER Aggro vs EMBER Aggro CPU:
passed mulligans, played Cinderling (1⚡ Unit) → picked "Arena Left"
→ Clash prompt "Attack" / "Hold" fired → clicked Attack →
`DamageDealt(source=#38, target=#67, counter=integrity, amount=2)` on
the log, CPU's Left Conduit integrity dropped from 7 to 5, game
advanced to CPU's Main phase. All other Conduits untouched.

The loop is complete. A human can click through a full Unit-based
combat turn against a CPU, using real decks, and see real damage
numbers. Remaining: multi-card priority per turn (Interrupts), frontend
per-arena Clash layout (8j is cosmetic — the action bar already
functions), and the Playwright e2e (8k) to drive this automatically in
CI.

173/173 tests green.

---

## 2026-04-19 — 8e: Unit play + Clash declaration + Force damage

Units now enter play. Clash now does something. The engine can reach
a TwoConduitsLost victory through pure combat — not just Maneuver
damage.

**Unit play.** `Builtins.PlayCard` dispatches on `type:` from the
CardDecl. Maneuvers keep the 8c path (Hand → Cache → OnResolve).
Units hit a new `PlayUnit`:

1. Publish an `InputRequest` with `Kind="target_arena"`, one
   `LegalAction` per `state.Arenas` entry (Metadata carries the
   arena's `pos` symbol).
2. On submission, move card Hand → `Player.Battlefield` (new zone in
   fixtures; real encoding needs a migration in its own commit).
3. Copy `force` + `ramparts` from the CardDecl onto the entity's
   counters. Set `type="Unit"`, `in_play=true`, `OwnerId=player`,
   `arena=<pos symbol>`, `arena_entity=<arena ref>`.
4. Fire `OnResolve` (most real Units don't have one; enter-the-
   battlefield triggers are separate) and enqueue `UnitEntered` +
   `CardPlayed` events.

**Clash.** `ResolveClashPhase(active_player: p)` replaces its void
stub. For each Arena in `state.Arenas`:

- Find the active player's Units whose `arena` parameter matches the
  Arena's `pos`.
- If any: emit `ClashBegin`, then for each Unit publish an
  `InputRequest` with `Kind="declare_attacker"` offering
  `attack` / `hold`. `attack` accumulates into a per-arena attacker
  list; `hold` is a no-op.
- After declarations, find the opponent's non-collapsed Conduit with
  matching `arena` pos; each attacker's `force` drains its integrity.
  Emits `DamageDealt` per hit and `ClashEnd` per arena.
- Multiple attackers in one arena stack on the same Conduit; 8f's
  SBA pass fires `ConduitCollapsed` when integrity hits 0.

Simplifications called out in the 08 plan: no blockers, no Interrupts,
no per-player defender step. One-directional Force → Conduit only.

Fixture `clash.ccgnf`: two Arenas (Left/Right), four Conduits via
`InstantiateEntity`, `Warrior` (force 2) and `BigWarrior` (force 7).
Six tests cover: Unit → Battlefield with stats and arena set, event
emission, attack damages opponent's same-arena Conduit, untouched
other-arena Conduit, hold deals no damage, no-unit Clash passes
through without prompting, and 7 force collapses a Conduit via 8f's
SBA pipeline.

173/173 tests green (+6 for 8e).

Open items still gating step 8's "Done when":

- No multi-card priority loop — Main phase is still single-shot, so
  a player can't play multiple cards in one turn. Interrupts land
  with the full priority window.
- Real encoding's Player has no Battlefield zone; Units-in-play need
  a zone migration (real encoding uses `Arena.units[Player]` child
  zones). Fixtures work; the real game still halts at deck-out
  unless the decks contain Maneuvers only.
- No frontend yet for declare_attacker / target_arena. 8j wires the
  Clash UI.

---

## 2026-04-19 — 8i: Readable action-bar labels

Small UX polish on top of 8c/8d/8h. The action bar was rendering raw
`play:57` / `target:4` labels because `InputPending` SSE frames only
carried a comma-joined string of `LegalAction.Label`s — the richer
`Metadata` (cardName, cost, displayName, kind) was dropped server-side.

- `Room.DriveRun` now serializes the full `LegalAction` list as JSON
  into a new `legalActions` field alongside the existing `options`
  string (kept for back-compat).
- `tabletop.ts` parses the JSON, stores an array of `{kind, label,
  metadata}`, and a new `humanizeAction` helper renders the shown
  text: `"Play Cinderhound (2⚡)"`, `"Target Conduit #67"`, `"Pass"`,
  etc. Falls through to the raw label for unknown kinds.
- Anonymous instances (`InstantiateEntity(kind: Conduit, …)` has
  `DisplayName == Kind`) collapse to `"Kind #id"` instead of "Conduit
  Conduit".

Verified in preview: full lobby → CPU + human join → mulligan-pass
flow, Main-phase buttons read `"Play Spark (1⚡)"`; clicking Spark
opens a target prompt whose buttons read `"Target Conduit #67"` ×
six (the six conduits in the real encoding).

---

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
