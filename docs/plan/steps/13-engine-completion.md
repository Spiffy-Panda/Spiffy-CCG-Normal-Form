# Step 13 — Engine completion (keyword wiring)

> **Status: open, in progress (2026-04-20).** Sub-steps A/B/C of §2 of
> [`../engine-completion-guide.md`](../engine-completion-guide.md)
> landed — Sentinel + Fortify are live, and the Clash phase uses the
> per-Arena incoming formula. Post-wiring benches confirm the stall
> cells break (see "Bench arc" below). Next keyword in the queue is
> **Triggered-on-Unit dispatch** per §3.2 of the guide. This file is
> the running status index for the rest of the arc.

Step 12 bounced off a finding that most ccgnf keyword macros
(`Sentinel`, `Fortify`, `Mend`, `Phantom`, `Shroud`, `Ignite`, `Drift`,
`Rally`, …) were declared in the encoding but never dispatched at
runtime — the v1 interpreter's `DispatchEvent` only iterated
`Game.abilities`, and Static / Replacement abilities on any non-Game
entity were parsed and then ignored. Step 12.5 / 12.6 remain paused
until enough of those keywords are live that a re-bench is measuring
the game the card text describes, rather than a subset.

The engine completion guide is the instruction doc; this step file
tracks progress against it.

## Scope — in

- Every keyword in `encoding/engine/03-keyword-macros.ccgnf` has a
  passing end-to-end probe in
  [`../../../tests/Ccgnf.Tests/KeywordWiringTests.cs`](../../../tests/Ccgnf.Tests/KeywordWiringTests.cs),
  per the per-keyword probe template in
  [`../engine-completion-guide.md`](../engine-completion-guide.md)
  §3.1.
- `Interpreter.DispatchEvent` walks every entity's abilities, not
  just `Game.Abilities`. Triggered, Static, and Replacement all
  dispatch — the first two are load-bearing, Replacement is the
  hardest and lands last.
- Card text that reads "at End of Clash, …", "when this enters play,
  …", "while controller's Conduit ≥ 4, …" actually fires during a
  bench match.
- `ResonanceField` is populated by `PushEcho` and readable by
  `CountEcho` / `Resonance` / `Peak`, so tier predicates (`Resonance(
  BULWARK, 3)`, `Peak(TIDE)`) stop returning false in every match.
- A re-bench of `PairCorrectly` + `AiDeckMatrix` returns a
  **materially different** set of numbers that can be explained
  card-by-card. Direction isn't specified — BULWARK walls will
  stiffen decisive wins in some cells and shed draws in others.

## Scope — out

- No new card authoring. The step 12.3 pool stands until this
  step's keyword surface is live and re-benched.
- No deck edits. The four reference decks at `encoding/decks/*.deck.json`
  are frozen for the duration of this step.
- No new engine knobs (Knobs 1 / 2 / 4 from the 12.2 audit). Those
  act on a damage model that, pre-wiring, doesn't include
  Fortification / Sentinel. Defer until keyword coverage is real.
- No AI weight retunes. Bot scoring reads the same `force` /
  `integrity` / `ramparts` today as it will post-wiring; the
  differences will show up as cell movement in the re-bench, not
  as a weight drift.
- No `Interrupt`-timing work. The play protocol's priority-window
  generalisation (and with it Debt accrual from opponent-turn plays)
  is a separate, larger piece. This step covers only keyword macros.

## Plan of work

Follow the priority order in
[`../engine-completion-guide.md`](../engine-completion-guide.md) §3.2.
Ship one keyword (or keyword family) per commit; each lands with a
`KeywordWiringTests` probe + a bench re-run artifact in
`ai-testing-data/post-wiring-<keyword>.*.results.json`.

1. **Fortify + Sentinel (Static dispatch, smallest slice).** Lands
   as three commits per guide §2.2 — Static evaluation, Fortify
   gate, per-Arena incoming formula. ✅ Landed 2026-04-20.
2. **Triggered-on-Unit dispatch.** Biggest single unlock by card
   count — every `OnEnter`, `OnArenaEnter`, `OnCardPlayed`,
   `EndOfClash`, `StartOfYourTurn`, `EndOfYourTurn`, `Event.PhantomReturn`
   trigger on a non-Game entity. Most of the step-12.3 closer pool
   (RampartCharger, SiegeCaptain, WatchtowerArcher, WallbreakerIncarnate,
   BishopOfMending, …) becomes real text the moment this lands.
   *Dispatch landed 2026-04-20* — `DispatchEvent` walks every entity,
   `PlaceUnit` copies the card's `abilities:` list onto the runtime
   Unit, `EnterPlay(target=self)` is now emitted, and the pattern
   matcher gained a `selfEntityId` path so `target=self` is an
   entity-ref equality check rather than a lowercase binding. One
   `DebtPinger.OnEnter` probe fires end-to-end. Remaining work on
   this wave: re-bench the full cards/*.ccgnf corpus (which already
   declares the shorthand `OnEnter` / `EndOfClash` / ...) — if the
   preprocessor expands those inside the corpus, the real closer
   pool should activate and move numbers again.
3. **Replacement-ability dispatch.** Unlocks Recur, Unique, Shroud
   target-legality, Harborkeeper's redirect. Harder than Triggered:
   Replacement interposes on an event *before* it commits, so the
   dispatcher needs a `replace_with` evaluation path and a guard
   predicate check.
4. **Phantom's schedule-at-end-of-clash dance.** Falls out of (2)
   plus a small `Phantoming`-state → Clash-contribution hook.
5. **Kindle + Ignite + Drift + Rally + Sprawl-keyword form.** All
   are just triggered-on-Unit consumers; they light up once (2) works.
6. **Surge cost reduction.** Low priority — fiddly because the cost
   is read at the play-protocol step, which itself needs
   generalising beyond the `cost:` field lookup.
7. **Reshape + Resonance Field + Banner.** Whole sub-system; verify
   `ResonanceField` is populated by `PushEcho` first. See the
   non-keyword gaps list in guide §3.4.

Non-keyword gaps to tackle alongside the above as they surface:
Debt accrual from Interrupts, `RevealHand`, `MoveTo(exiled)`,
`Heal` upward, `SwapEchoPosition`, `Ramparts heal at End of Fall`.

## Tests

- **Every wired keyword gets a probe.** Pattern lives in
  [`../../../tests/Ccgnf.Tests/KeywordWiringTests.cs`](../../../tests/Ccgnf.Tests/KeywordWiringTests.cs).
  Fixtures under `tests/Ccgnf.Tests/fixtures/` — extend or add one
  per keyword as needed; don't try to shoehorn every probe into
  `clash-sentinel.ccgnf`.
- **`make test` must stay green.** No regression in existing Clash
  / Conduit-collapse / Target / InterpreterRun suites.
- **Post-wiring bench is the integration check.** If a wiring commit
  doesn't move the bench in a way that can be explained card-by-card,
  the wiring is probably wrong (wired in the wrong pass, guard
  predicate inverted, filter mismatching) — re-debug before moving on.

## Exit criteria

Mark step 13 closed when all of these are true:

- [ ] Every keyword in `encoding/engine/03-keyword-macros.ccgnf` has a
      passing probe in `KeywordWiringTests`.
- [x] Triggered dispatch walks every entity's abilities, not just
      `Game.Abilities`. *(partially — Static dispatch for Sentinel /
      Fortify via KeywordRuntime helper; full Triggered-on-Unit
      dispatch is sub-step 2 above.)*
- [x] Static dispatch evaluates at least at start-of-Clash and
      applies layered modifiers (matching layer 2/3 semantics in
      `common/01-schema.ccgnf`). *(via KeywordRuntime; the full
      layer system lands later.)*
- [ ] Replacement dispatch walks matching events before commit;
      at least Recur on one TIDE Unit or `Harborkeeper`'s redirect
      has an integration test.
- [ ] `ResonanceField` is populated by `PushEcho` and read by
      `CountEcho` / `Resonance` / `Peak`.
- [ ] PairCorrectly at baseline seed 1 returns materially different
      numbers from `ai-testing-data/post-knob3.PairCorrectly.results.json`
      in a way you can explain card-by-card.

## Bench arc

Post-wiring bench artifacts land at
`ai-testing-data/post-wiring-<thing>.*.results.json` so the next
session can diff them trivially. Always pair a keyword commit with
a same-session bench run; the guide's §5 explains the convention.

### Fortify + Sentinel (2026-04-20)

`ai-testing-data/post-wiring-fortify.PairCorrectly.results.json`
vs `post-knob3.PairCorrectly.results.json`:

| Metric                     | Pre-wiring | Post-wiring | Δ         |
|----------------------------|-----------:|------------:|----------:|
| Draw rate (PairCorrectly)  | 37.1 %     | **21.7 %**  |  −15.4 pp |
| BulFort decisive WR        | 36.1 %     | 71.6 %      |  +35.5 pp |
| TiThWave decisive WR       | 95.8 %     | 88.0 %      |   −7.9 pp |

`ai-testing-data/post-wiring-fortify.AiDeckMatrix.results.json`
vs `ai-testing-data/AiDeckMatrix.results.json` (720 games):

| Per-deck decisive WR | Pre-wiring | Post-wiring | Δ         |
|----------------------|-----------:|------------:|----------:|
| bulwark-ctl          | 29.3 %     | 66.9 %      | +37.6 pp  |
| ember-aggro          | 30.1 %     | 26.8 %      |  −3.3 pp  |
| hollow-disr          |  9.8 %     |  8.8 %      |  −1.0 pp  |
| tide-thorn           | 87.5 %     | 82.4 %      |  −5.1 pp  |

Stall-cell movement (the guide's ≥ 5 pp integration check):
**BulFort-vs-EmbHell (bulwark-ctl × hellfire) moved +56.8 pp
decisive WR** (10.3 % → 67.1 %). This is the smoking gun that the
wiring reaches the Clash math — BULWARK is actually a wall now, and
EMBER's raw-Force pressure is absorbed by Fortification before it
touches Conduit integrity. Per-AI aggregates stay tight (within
~5 pp of 50 %), so the bots haven't lost the plot.

## Non-blocking reminders

- Don't touch cards / decks to "compensate" for unwired keywords.
  Any edit made to force bench movement pre-wiring becomes debt the
  moment the keyword lights up. Guide §6.
- Don't ship engine knobs until the keyword pass has coverage. Tuning
  space pre-wiring is cosmetic.
- Don't rewrite the card-cluster skill or re-author cards until
  Triggered-on-Unit dispatch works. The skill is fine; cards are
  fine; the interpreter is what's behind.
