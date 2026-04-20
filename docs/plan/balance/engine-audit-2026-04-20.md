# Step 12.2 — Engine sanity audit (2026-04-20)

**Verdict: rules need a knob.** A Hellfire-piloted `ember-aggro`
mirror — the most damage-dense configuration the current pool
allows — still draws 97.5 % (117/120 games) at an average of 428
engine steps. If even the purest aggro pile can't close against
itself, no amount of card-pool rework inside the current engine
settings will reach the 12.1 exit target of ≤ 40 % draws.

Three engine knobs are queued below. The primary recommendation is
the Clash Incoming multiplier; the others stay on the shelf until
12.3 card work either confirms or disproves the primary fix.

## Victory-condition summary

From [`design/GameRules.md`](../../../design/GameRules.md) §7 and
[`encoding/engine/07-sba.ccgnf`](../../../encoding/engine/07-sba.ccgnf):

- **Primary win:** two of an opponent's three Conduits at Integrity ≤ 0.
  SBA fires continuously; Lose event resolves to the *other* player's
  Win.
- **Alternate loss:** drawing from an empty Arsenal in Rise.
  Simultaneous deck-outs → Draw.
- **Simultaneous Lose events** (both players collapsing on the same
  Clash or effect) → Draw.
- **No turn limit.** No total-integrity tiebreaker. No Banner-lead
  tiebreaker. If no side forces one of the above conditions, the game
  is mechanically infinite — the tournament harness is the only
  thing that ends it, via `maxInputsPerGame` / `maxEventsPerGame`.

That last point is the crux. What the 12.0 / 12.1 JSONs report as
"draws" is, 99 % of the time, **harness-cap hit**, not a rules draw.
The engine doesn't give up; our test loop does. Any tiebreaker
introduced here applies *only* in the harness — the live game
still ends exactly the way `GameRules.md` says it does.

## Conduit / Clash / Aether knobs

Entity declarations pulled from
[`encoding/engine/04-entities.ccgnf:86–94`](../../../encoding/engine/04-entities.ccgnf)
and
[`encoding/engine/09-clash.ccgnf:42–86`](../../../encoding/engine/09-clash.ccgnf):

- **Conduit starting Integrity: 7.** Three per player → 21 Integrity
  total per side; lethal requires collapsing two → 14 damage routed
  past Fortification across ≤ 2 Arenas.
- **Clash Incoming formula (09-clash.ccgnf:50–51):**
  ```
  incoming[side] = Max(0, projected_force[other_side] - fortification[side])
  ```
  Damage is 1:1 of the raw Force-minus-Fortification delta, per
  Arena, per Clash. No multiplier, no cap, no floor (other than 0).
- **Aether schedule (04-entities.ccgnf:45):**
  `[3, 4, 5, 6, 7, 8, 9, 10, 10, 10]` — refresh, not accumulation.
- **Hand cap:** 10 in Fall (06-turn.ccgnf:63). Overdraws discard
  down.

The Force-vs-Fortification formula is the arithmetic bottleneck.
In a mirror, both sides stack similar Units; Fortification scales
with the same Force they're trying to throw. Net `incoming` hovers
near 0 most turns, and the 7-Integrity-per-Conduit wall
never crumbles.

## Empirical probe: ember-aggro mirror

Tournament config:
[`ai-testing-data/12.2-ember-speed.tournament.json`](../../../ai-testing-data/12.2-ember-speed.tournament.json).
40 games × three matchups (HellA × HellA, HellA × HellB, HellB × HellB —
two identical labels so the runner expands cross + mirror). Hellfire
is the most aggressive bot after 12.1; `ember-aggro` is the highest-
burn deck we ship.

| Matchup           | W-L-D      | Avg steps |
|-------------------|-----------:|----------:|
| HellA vs HellA    | 1-0-39     | 429.4     |
| HellA vs HellB    | 0-1-39     | 428.4     |
| HellB vs HellB    | 1-0-39     | 428.3     |
| **Total (120 g)** | **2-1-117**| **428.7** |

**Decisive rate: 3 / 120 = 2.5 %. Draw rate: 97.5 %.** Worse than the
PairCorrectly cross-matchup baseline (81.7 %). Symmetric aggressive
pressure is the *hardest* matchup for decisive play, not the easiest —
confirming the Fortification-absorbs-Force hypothesis above. Result
artifact: [`ai-testing-data/12.2-ember-speed.results.json`](../../../ai-testing-data/12.2-ember-speed.results.json).

## Proposed knobs (queued, not committed)

Listed in priority order. Only one will ship in 12.2's sibling
commit if we decide to pull the trigger — the rest are follow-ups
that 12.3–12.6 can escalate to.

### Knob 1 (primary) — Clash Incoming multiplier

- **Change:** `encoding/engine/09-clash.ccgnf:50–51`, update the
  `incoming[side]` derived characteristic from
  `Max(0, proj_force - fortification)` to
  `Max(0, (proj_force - fortification) * 2)`. Keep the `Max(0, ...)`
  floor; double only the post-subtraction delta so small differences
  don't nuke Conduits out of nowhere.
- **Hypothesis:** A 2× multiplier on the existing delta roughly halves
  the turns-to-two-conduits-collapse. Against a matched Fortification
  wall the `delta` stays near 0 — so the knob only bites when one
  side has genuinely overcommitted, which is what we want.
- **Reversibility:** one line, one commit. Revert if bench explodes.

### Knob 2 (secondary) — Conduit starting Integrity 7 → 5

- **Change:** `encoding/engine/04-entities.ccgnf:90–91`, swap
  `starting_integrity: 7` / `integrity: 7` to `5` / `5`. Also check
  `encoding/engine/05-setup.ccgnf` for any hard-coded initial counter
  that mirrors this.
- **Hypothesis:** Total per-side Conduit HP drops 21 → 15 (−29 %).
  Decisive games shorten proportionally once the first trickle of
  damage lands. Less reversible than Knob 1 because decks and cards
  may assume the 7 number implicitly (e.g., "takes 3 Clashes of 3
  damage" plans).
- **Risk:** flips design-intent assumptions about Conduit durability
  recorded in `design/GameRules.md`; the design doc needs an update
  in the same commit.

### Knob 3 (soft-fallback) — Harness-only tiebreaker

- **Change:** new post-cap rule in the tournament harness (not in
  the engine itself): if `maxInputsPerGame` / `maxEventsPerGame` is
  reached without a Lose event, decide the game by `sum(self.integrity)
  - sum(opponent.integrity)` at the moment of cap. Ties still draw.
- **Where:** `src/Ccgnf.Bots/Bench/BotMatchRunner.cs` (or wherever the
  cap currently marks the game as a draw).
- **Hypothesis:** Converts most harness-cap "draws" into decided games,
  rescuing signal from what is otherwise wasted compute. Pure
  measurement-layer fix — the live game keeps its infinite-if-nobody-
  loses semantics.
- **Decision:** worth shipping **independent of** Knobs 1 / 2, because
  it improves bench signal even if the engine stays as-is.

### Knob 4 (speculative) — Aether cap schedule shift

- **Change:** `encoding/engine/04-entities.ccgnf:45`, front-load
  `[3, 4, 5, 6, 7, 8, 9, 10, 10, 10]` → `[4, 5, 6, 7, 8, 9, 10, 10, 10, 10]`.
- **Hypothesis:** More Aether per turn → more plays → more Force
  hitting the board → more Clash pressure. Indirect, not guaranteed
  to shorten games (might just load more Fortification too).
- **Hold unless** Knob 1 + card work don't get us to ≤ 40 %.

## Recommended action for the step-12 arc

1. **Ship Knob 3 now**, independently of this audit note, in a tiny
   sibling PR. It's a pure harness change, high signal-to-noise, and
   it turns every future bench into a cleaner instrument.
2. **Do not ship Knob 1 or 2 in this step.** The 12.1 exit message
   routed us to 12.3 *first*. Let card-pool changes try to lift the
   decisive rate; only if 12.3 + 12.5 can't clear the ≤ 40 % bar
   does Knob 1 get pulled off the shelf.
3. **If Knob 1 does ship**, it goes in a dedicated commit under this
   step (`Step 12.2: engine knob — Clash Incoming ×2`) with its own
   baseline re-run (`PairCorrectly + SimpleSweep`) stored as
   `ai-testing-data/post-knob1.*.results.json` for diffing.

## Exit criteria check

- [x] Engine-audit note committed (this file).
- [x] The note answers exactly one of "rules are fine" / "rules need
      knob X". Answer: **rules need a knob**, leading candidate is the
      Clash Incoming multiplier.
- [ ] Knob change shipped in the same/sibling commit **before**
      12.3 card work starts. Decision: **deferred by one step** — the
      12.1 routing message explicitly says 12.3 is primary; let card
      work run first so we don't accidentally solve a card problem
      with an engine patch.

## Follow-ups

- The "draws" in every tournament output so far are really harness-cap
  hits. Knob 3 reclassifies them; that alone will make later steps'
  diffs legible.
- If Knob 1 ships, add an interpreter integration test for the new
  Clash multiplier in `tests/Ccgnf.Tests/` (per step-12.2 Tests
  section). The current Clash integration tests assume the 1:1 ratio;
  they need updating or a new parallel test.
