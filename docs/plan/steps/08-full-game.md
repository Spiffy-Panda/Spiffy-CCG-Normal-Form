## Step 8 — First full game played through the web tab

Playable: open the Play tab, create a room with 1 CPU, join as the
human, and reach a `GameEnd` event — the interpreter transitions
through Setup, multiple turns of Rise / Main / Clash / Fall,
and halts on a legal victory condition (the first player to lose all
three Conduits in an Arena, or whichever simpler subset of §7.5 we
land first). Along the way the human submits real moves by clicking
cards and targets on the board; the CPU moves on `GetLegalActions[0]`.

**Exit criteria:**

- Human vs CPU game reaches `GameEnd` with a non-trivial move sequence
  (not all-pass). CPU plays a card at least once.
- All human-driven interactions work via preview click events — that
  is: Playwright / `preview_click` can drive a game end-to-end from
  `#/play/lobby` to the terminal `RoomFinished` frame.
- `StateSerializer` round-trips same seed + same action sequence to
  identical bytes (determinism regression still holds).
- Each sub-commit lands with its own tests + a short devlog entry.

Read before starting:
[`design/GameRules.md`](../../../design/GameRules.md) §4–§7 (setup,
turn structure, clash), [`reference/interpreter.md`](../reference/interpreter.md),
[`reference/builtins.md`](../reference/builtins.md), and the relevant
encoding: [`encoding/engine/`](../../../encoding/engine/).

## Scope boundaries

**In scope:** Mulligan firing; turn rotation; card play with cost +
single-target effects; at least two card shapes (Unit entering an
Arena + Maneuver that damages a target); Clash damage to Conduits;
Conduit collapse SBA; one victory condition (3 arenas with opponent's
conduits gone → arena control win, or equivalent).

**Out of scope:** Interrupts / Debt; Phantom; Push / Echo mechanics;
Replacements; full Resonance-tier scaling; hand-cap enforcement;
Standards-on-Arena lifecycles; Alternate-cost mechanics. These are
noted as follow-ups below, not blockers on 8's exit.

## Resolved design choices (or: things we won't re-debate)

- **Engine-first, UI-after.** Each sub-commit extends the engine with
  a new ability class, adds or exercises the builtin(s) it needs,
  covers it with unit tests, then only exposes it through the web
  once the `InputRequest` / `LegalAction` shape for that interaction
  is stable. Frontend never leads — if the engine can't do it, the UI
  doesn't pretend it can.
- **`Game.max_mulligans` — fix the encoding.** Today it's declared on
  Player and read from Game, so Mulligan collapses to `Repeat(0, …)`.
  8a fixes the reference and makes Mulligan fire.
- **Legal-action kinds multiply.** 7f only surfaced `choice_option`.
  We'll add `play_card`, `target_entity`, `declare_attacker`,
  `pass_priority` as each mechanic lands. The `LegalAction` record
  already carries `Kind + Label + Metadata` — no schema change needed.
- **CPU baseline stays first-legal.** A smarter bot can plug in via
  `IRoomBot` later; 8 uses `LegalActions[0]` throughout. Good enough
  to prove the loop.
- **One card per archetype, not ten.** Each mechanic gets the single
  simplest card from the encoding that exercises it. Don't wire a
  full deck until the mechanic is tested in isolation.

## Dependency order

```
8a Mulligan + turn rotation  ──┐
                                ├─> 8c Card play: cost + no-target effect
8b Phase skeleton events  ────┘              │
                                              ├─> 8d Target resolution (single Unit/Conduit)
                                              │              │
                                              │              ├─> 8e Clash: declare + damage
                                              │              │              │
                                              │              │              ├─> 8f Conduit collapse SBA
                                              │              │              │              │
                                              │              │              │              ├─> 8g Victory: arena control
                                              │              │              │              │              │
8h Frontend: turn chip / active-player hint ──┴──────────────┴──────────────┴──────────────┤
                                                                                            │
8i Frontend: play-card + target clicks (hand → legal highlights → submit)  ─────────────────┤
                                                                                            │
8j Frontend: clash declaration UI  ─────────────────────────────────────────────────────────┤
                                                                                            │
8k Playwright end-to-end: lobby → CPU + human → GameEnd  ───────────────────────────────────┘
```

## Sub-commits

Each is its own PR-shaped commit. Format follows 7a–7h.

### 8a. Mulligan fires; turn rotation lands

- **Encoding.** `Game.max_mulligans` → `first_player.max_mulligans` (or
  lift to a Game characteristic). Verify Mulligan's `Repeat` pulls a
  non-zero value. Update `encoding/engine/05-setup.ccgnf`.
- **Engine.** Handle the `Target` builtin well enough to return from
  `PerformMulligan` — selector evaluator accepts predicates over
  lists of card entity ids; Mulligan-specific branches re-draw.
  `MoveTo` moves entities between zones.
- **Turn rotation.** After Round-1 Rise, emit `PhaseEnd(Rise)` →
  `PhaseBegin(Main)` → `PhaseBegin(Clash)` → `PhaseBegin(Fall)` →
  swap active player → next Rise. Main / Clash / Fall are stubs
  that immediately emit `PhaseEnd`. Active player rotates each Fall.
- **Tests.** Mulligan with 2 returned cards removes them from hand,
  reshuffles, re-draws (n−1). Turn rotation: three round-trips of
  phase events, active player alternates.

### 8b. Phase skeleton events

Split out from 8a if 8a gets too big. Just emit the phase-begin /
phase-end events with no interior; the interior lands in 8c.

### 8c. Card play — cost + no-target effect

- **Engine.** Main phase opens a **priority window** where the active
  player picks from `LegalActions = [pass] ∪ {play_card:<entity_id>}`.
  Submitting a `play_card` input:
  - Validates the card is in their Hand.
  - Pays aether.
  - Resolves `on_resolve:` effect (for no-target cards only — start
    with `Spark` from `encoding/cards/ember.ccgnf`: "deal 1 damage to
    target Conduit" → *target* version comes in 8d. Instead pick the
    simplest no-target card in the corpus; if none exist, synthesize
    `Cinderling` → "enters play" is a mid-effect. Actually the
    simplest candidate is a Unit that just enters an Arena with no
    targeting — see `Thornpup` or similar.).
  - Moves the card Hand → ResonanceField (Echo push) → Cache.
- **InputRequest.** Publish legal-actions with `Kind = "play_card"`,
  `Label = entity_name`, `Metadata = { entityId }`. Host serializes
  into SSE fields.
- **Tests.** Given a one-card hand and enough aether, submit play →
  state shows card in Cache, hand smaller, Echo in ResonanceField,
  aether spent.

### 8d. Target resolution — single target

- **Engine.** `Target(selector, chooser, bind)` becomes a suspension
  point: evaluate selector over GameState entities, publish
  `InputRequest{ Kind:"target_entity", LegalActions: [entity ids as
  labels] }`, block, resume with picked entity.
- Pick a Maneuver from `encoding/cards/ember.ccgnf` that deals damage
  to a target Conduit. Exercise cost + target-pick + effect +
  counter decrement.
- **Tests.** Play a 1-cost "deal 2 to any Conduit" card → target a
  specific opponent Conduit → its `integrity` decreases by 2.

### 8e. Clash: declaration + damage

- **Engine.** Clash phase iterates Arenas. Active player declares
  attackers per Arena (each Unit chooses attack / hold, default hold).
  Defender assigns blockers likewise. Damage resolves per §7.1–§7.3.
  Simplified: no tricks, no interrupts. Just "attacker.force → opponent
  conduit".
- **InputRequest.** `Kind = "declare_attacker"` for each of the active
  player's Units per Arena, with `[attack, hold]` options.
- **Tests.** One Unit on board, declare attack → opponent's Conduit
  loses Integrity.

### 8f. Conduit collapse SBA

- **Engine.** Wire the first real SBA: `Conduit.integrity <= 0 ⇒
  Conduit.collapsed = true; emit ConduitCollapsed`. Runs once per
  event loop pass.
- **Tests.** Damage Conduit to 0 → collapse event fires in the next
  SBA pass → collapsed flag set.

### 8g. Victory condition — arena control

- Pick a single victory condition from §7.5: "If a player has 0
  standing Conduits, they lose." Emit `GameEnd(loser:<pid>,
  reason:ConduitsLost)`.
- **Tests.** Collapse all 3 Conduits of Player2 across 3 Arenas →
  `GameEnd` event → `state.GameOver == true`.

### 8h. Frontend — turn chip + active-player hint

- Tabletop topbar shows a turn indicator ("Your turn" / "CPU1's turn")
  derived from the latest `PhaseBegin` + active player id.
- Seat-strip highlights the active seat (already has `.seat-active`
  class; ensure it's computed from `view.activePlayerId`).
- Fix the [viewer entity-id mismatch](../../../web/src/shared/board.ts)
  noted in 7h's devlog so viewer / opponent are correctly oriented.
- Engine banner stops saying "later phases arrive with 7f".

### 8i. Frontend — hand-click → target → submit

- Click a card in Hand: if there's a `play_card` legal action for its
  entity id, highlight the card + show a "Play" chip.
- Click the Play chip: if the card needs a target, publish a
  board-level highlight of legal targets (from the next InputPending
  frame). Click a legal target → submit a compound action frame
  (or two submits back-to-back: play_card, then target_entity).
- Existing inspector still opens on right-click / long-press; left
  click enters the play-card flow when applicable.
- **Verification:** `preview_click` on a hand card → opponent Conduit
  integrity drops in the next SSE frame.

### 8j. Frontend — clash declaration UI

- On `PhaseBegin(Clash)`, show a per-Arena row of attacker toggles
  (click-to-attack, click-again-to-hold). "Confirm attacks" button
  submits all declarations.
- CPU's attack declarations arrive as `CpuAction` frames the client
  already renders.

### 8k. Playwright end-to-end

- New Playwright spec under `web/tests/e2e/full-game.spec.ts`:
  1. Navigate `#/play/lobby`.
  2. Add 1 CPU seat with preset `tide-thorn-combo`.
  3. Click Create.
  4. Pick preset `ember-aggro`, click Claim a seat.
  5. Loop: for every `InputPending` SSE frame targeting the human
     seat, click the first legal-action button. Break on
     `RoomFinished`.
  6. Assert the SSE log contains at least one `GameEvent(ConduitCollapsed)`
     and a terminal `GameEnd`.
- Run the spec in `make ci` on a matrix job; keep it isolated so a
  flaky interpreter step doesn't block unrelated unit tests.

## Testing strategy

- **Unit tests per engine mechanic** (8a–8g): added in
  `tests/Ccgnf.Tests/` alongside existing `InterpreterTests` /
  `InterpreterRunTests`. No room / REST involvement — direct against
  `Interpreter.StartRun`.
- **Integration tests per endpoint change** (as-needed): in
  `tests/Ccgnf.Rest.Tests/`.
- **Preview click-driven smoke tests** at 8h, 8i, 8j: ad-hoc in the
  PR for the step, via `preview_click` / `preview_eval`, recorded as
  a screenshot in the commit body.
- **End-to-end automated** at 8k: Playwright, CI-wired.

## Likely risks

- **Mulligan `Target` evaluator may drag in more selector grammar
  than we want in 8a.** If so: punt on the "return N cards, reorder
  them" branches; let Mulligan always pick a fixed subset for the
  test and come back later. Ruling R-M in `encoding/engine/00-rulings.ccgnf`
  can document the shortcut.
- **Priority windows / timing-window builtins** are a rabbit hole.
  Stay minimal: Main phase = one decision per active player, then
  pass. Don't implement interrupts.
- **Frontend state for "which pending belongs to whom" is already
  shaky** — we patch around the entity-id / roster-id mismatch in
  several places. 8h should unify this. Consider exporting
  `pending.rosterPlayerId` from the backend instead of inferring in
  TS.
- **Clash damage encoding is dense** — the existing
  `encoding/engine/09-clash.ccgnf` may be further along than the
  engine; pick the minimum slice that executes.

## Done when

- Cold session: open `#/play/lobby`, add a CPU (EMBER), join as human
  (HOLLOW) — play through, see the `GameEnd` SSE frame, screenshot
  the final board state with a collapsed-Conduit indicator.
- Playwright e2e passes in CI.
- Devlog entry per sub-commit; README status row bumped (web-app row
  from "Playtest v1" to "Playable", engine row threshold crossed).
- Follow-ups parked: Interrupts, full Resonance scaling, Phantom,
  hand-cap discard, Standards lifecycles, smarter CPU.
