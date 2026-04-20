# 4 AIs × 4 decks matrix — 2026-04-20

**Purpose:** triangulate "which pilot plays which archetype best" by
running every AI against every deck in the same tournament. 16 pairs,
cross-round-robin, 6 games per cell, 720 games total. Diagnostic for
[`12.5`](../steps/12.5-matched-ai-tune.md) matched-AI tune.

**Config:** [`ai-testing-data/AiDeckMatrix.tournament.json`](../../../ai-testing-data/AiDeckMatrix.tournament.json).
**Results:** [`ai-testing-data/AiDeckMatrix.results.json`](../../../ai-testing-data/AiDeckMatrix.results.json).
Knob 3 (integrity-delta tiebreaker) active.

## Headline numbers

- **Total:** 720 games, 310 draws (43.1 %).
- **Dominant pair:** He/TC — hellfire piloting tide-thorn-combo —
  85-3-2, decisive WR 96.6 %.
- **Broken pair:** every hollow-disruption pairing — best pilot posts
  12.5 % decisive WR.
- **The deck dominates the pilot.** All four AIs aggregate to
  roughly 49-51 % decisive WR across the matrix. The *deck* picks
  the win rate.

## The matrix (decisive WR, 6 games/cell)

|              | **fortress** | **hellfire** | **reaper** | **wavebreaker** | **deck avg** |
|--------------|-------------:|-------------:|-----------:|----------------:|-------------:|
| **bulwark-control**   |  **41.0 %** |  10.3 % |  31.6 % |  29.4 % | 29.3 % |
| **ember-aggro**       |  30.6 % |  16.1 % |  32.5 % | **38.5 %** | 30.1 % |
| **hollow-disruption** |  11.1 % |   4.9 % |  10.2 % | **12.5 %** |  9.8 % |
| **tide-thorn-combo**  |  82.8 % | **96.6 %** |  86.2 % |  84.3 % | 87.5 % |
| **AI avg**            |  50.2 % |  50.3 % |  49.1 % |  50.5 % |        |

Bold = best pilot per row (per deck).

---

## Per-deck review

### bulwark-control — deck avg 29.3 % decisive WR

**Best pilot: fortress (41.0 %).** The intended match; the deck's
Sentinel/Fortify/Mend core rewards fortress's high `threat_avoidance`
and moderate `conduit_softness`. The deck is playable but not
dominant — closers get stuck behind the defensive spine during the
stabilisation phase.

- fortress 41.0 % — correct match; still has headroom if the deck's
  closer count nudges up or the archetype re-tags as `control` pure.
- reaper 31.6 % — a removal-first pilot can't make use of the
  Sentinel-into-siege plan; treats BULWARK's bodies as generic
  value pieces.
- wavebreaker 29.4 % — `on_curve` 3.0 pushes plays too fast for a
  control deck that wants to bank Aether for interrupts.
- hellfire 10.3 % — **do not pair.** Reckless aggro profile wastes
  the deck's healing lines on units that don't threaten opposing
  Conduits directly, and skips Mend for face-damage that the deck
  can't sustainably deliver.

**Recommendation:** keep fortress as `suggested_ai`; 12.5 should
squeeze another 5-10 pp out of fortress's weights specifically
against the hellfire+reaper mirrors (where this deck currently
draws heavily).

### ember-aggro — deck avg 30.1 % decisive WR

**Best pilot: wavebreaker (38.5 %).** Surprise — the intended
pilot (hellfire) is the *worst* fit at 16.1 %. Wavebreaker's
higher `on_curve` + `tempo_per_aether` weights produce a cleaner
curve on a low-curve deck that *needs* its curve to hit.

- wavebreaker 38.5 % — high `on_curve` (3.0) + high
  `tempo_per_aether` (3.0) is actually a better aggro profile than
  hellfire's current shape. The 1.5 `threat_avoidance` also keeps
  wavebreaker from throwing key threats into bad trades.
- reaper 32.5 % — does fine; removal-first still lets the burn
  spells through because they target Conduits, not the removed
  Units.
- fortress 30.6 % — manages a curve-out even with defensive
  weights, because the deck's cards do the work.
- hellfire 16.1 % — **the current `suggested_ai` pairing is the
  worst on the matrix.** The all-in aggression profile
  (`threat_avoidance` 0.0 across every intent) means hellfire
  auto-commits threats that wavebreaker would hold one turn for a
  better attack, and loses them to removal that costs less than
  the Unit.

**Recommendation:** repin `ember-aggro.suggested_ai` to
**wavebreaker** pending 12.5, or redesign hellfire's `default` /
`early_tempo` to lift `threat_avoidance` off 0.0 without sacrificing
its aggro identity.

### hollow-disruption — deck avg 9.8 % decisive WR

**Best pilot: wavebreaker (12.5 %).** Nobody wins with this deck.
Every AI's ceiling on it is terrible — 4.9-12.5 %. This is a deck
problem, not an AI problem.

- wavebreaker 12.5 % — marginally less bad than the rest.
- fortress 11.1 %, reaper 10.2 % — statistically tied.
- hellfire 4.9 % — aggro profile on a disruption deck is a
  particularly bad pairing: burns the removal suite to chip a
  Conduit that's always healing.

**Recommendation:** this is a [`12.6`](../steps/12.6-cross-matchup-polish.md)
tech-slot problem at minimum, and might need a [`12.3`](../steps/12.3-card-threat-audit.md)
re-opener for one or two real Force-3+ HOLLOW bodies. AI retune
can at most add 2-3 pp to the ceiling; the deck fundamentally
cannot press a Sentinel-wall deck without board presence, and
HOLLOW's Phantom / Shroud bodies top out at Force 4 on two cards
total. A single new Force-3/3 Shroud Unit at curve 3 would probably
lift the ceiling more than any weight tuning can.

### tide-thorn-combo — deck avg 87.5 % decisive WR

**Best pilot: hellfire (96.6 %).** This deck is *too strong*,
irrespective of pilot. Worst pilot still posts 82.8 %.

- hellfire 96.6 % (85-3-2!) — hellfire's reckless Conduit focus
  amplifies TIDE's natural Reshape-payoff pressure. Every other
  cell is within 4 pp of the intended wavebreaker pairing.
- reaper 86.2 % — removal-first pilot on a combo deck still wins
  because the deck closes faster than reaper's default preferences.
- wavebreaker 84.3 % — the intended pilot; fine but not the best.
  Its moderate `threat_avoidance` (1.5) costs it a few games
  against fortress's BULWARK control pile that a 0-ta pilot just
  kills through.
- fortress 82.8 % — still dominates; even a defensive pilot can
  pilot the combo deck to 82 % because the deck's closers are
  strong enough regardless.

**Recommendation:** **pull TiThWave's power back at the deck level.**
Candidates:

1. Cut `DriftStriker` from 3 → 2 (it's the cheapest Force-3 closer;
   fewer copies = slower curve).
2. Cut `WaveCount` (Cache-scaling Maneuver) from 2 → 1.
3. Replace 1 Ripplekin / Thornling with a Refract (bounce).

Or, at the card level, tone `TidebreakerSage` (5-Aether Unit, Peak
TIDE → 3 damage per Fall) or `OvergrowthSurge` (Sprawl-count
Maneuver, cap 4). Both are load-bearing for the 90 %+ WR.

AI retune will *not* fix this — hellfire on the current deck
clocked 96.6 %, so pulling wavebreaker back helps the one pairing
but doesn't touch the deck's ceiling across pilots.

---

## Per-AI review

Each AI was tested on all four decks × cross-matchups, so each row
is 360 games (90 per deck, aggregated over cross-matchups).

### fortress — 50.2 % overall decisive WR

Designed for `bulwark-control`. Best on its intended deck (41.0 %),
worst on HOLLOW / TC combo (where its defensive weights don't fit).

| Deck | decisive WR | read |
|------|------------:|------|
| bulwark-control | 41.0 % | **correct pairing** |
| ember-aggro | 30.6 % | passes because EMBER cards do the work |
| hollow-disruption | 11.1 % | terrible match — defensive weights on a reactive deck |
| tide-thorn-combo | 82.8 % | deck carries; weights don't matter |

**Affinity: control / midrange.** Floor is clean (`conduit_softness`
≥ `threat_avoidance` in `pushing` + `lethal_check` satisfied via
12.1 pass). Ready for `stable/` promotion once 12.5 confirms the
matched-pair contract (≥ 15 pp over other AIs on BC) — currently
fortress beats the next-best BC pilot (reaper 31.6 %) by 9.4 pp,
which is short of the 15 pp bar.

### hellfire — 50.3 % overall decisive WR

Designed for `ember-aggro`. **Does not play its intended deck
well.** Actually the best pilot for tide-thorn-combo by a wide
margin, but pathologically bad on every other non-TC deck.

| Deck | decisive WR | read |
|------|------------:|------|
| ember-aggro | **16.1 %** | **the `suggested_ai` pairing is the worst deck cell for this pilot** |
| bulwark-control | 10.3 % | expected — aggro weights on a control deck |
| hollow-disruption | 4.9 % | worst cell in the whole matrix |
| tide-thorn-combo | 96.6 % | best cell in the whole matrix |

**Affinity: combo / tempo, not aggro.** Hellfire's
`threat_avoidance: 0.0` across every intent is the specific
problem. On a low-curve aggro deck, you need to hold threats
sometimes; hellfire can't. Fix options:

1. **Repin `ember-aggro.suggested_ai` → wavebreaker** (fastest).
2. **Edit hellfire's `default` / `early_tempo`** to raise `threat_avoidance` off 0.0 (to 0.3-0.5) and drop `opponent_priority` slightly. Preserves hellfire's identity for `lethal_check` and `pushing` where `ta: 0.0` still makes sense, but teaches it to hold a Unit one turn.

**Not ready for `stable/` promotion.** Fails the matched-pair
contract on ember-aggro by a huge margin.

### reaper — 49.1 % overall decisive WR

Designed for `hollow-disruption`. **Identity-constrained: does fine
on every deck, exceptional on none.** Removal-first weights are
generic-utility.

| Deck | decisive WR | read |
|------|------------:|------|
| ember-aggro | 32.5 % | surprisingly competent — burn spells close the game despite removal priority |
| bulwark-control | 31.6 % | generic-midrange pilot on a defensive deck |
| tide-thorn-combo | 86.2 % | deck carries |
| hollow-disruption | **10.2 %** | **even the intended pilot can't close with this deck** |

**Affinity: midrange / disruption.** Passes the floor rule. The
intended HOLLOW pairing posts 10.2 %, barely better than hellfire's
4.9 %. This is a deck problem surfacing as an AI problem — reaper
is the right kind of pilot for HOLLOW, it just can't do what the
deck asks.

**Not ready for `stable/` promotion.** Fails the matched-pair
contract on hollow-disruption; wavebreaker actually pilots HOLLOW
marginally better (12.5 % vs 10.2 %), so the pairing is wrong on
paper. Revisit after [`12.6`](../steps/12.6-cross-matchup-polish.md)
tech slots land.

### wavebreaker — 50.5 % overall decisive WR, top AI by aggregate

Designed for `tide-thorn-combo`. Actually the most *versatile* bot
— top or near-top on every deck including the ones it wasn't
designed for.

| Deck | decisive WR | read |
|------|------------:|------|
| ember-aggro | **38.5 %** | **better pilot for EMBER than hellfire** |
| bulwark-control | 29.4 % | competent |
| tide-thorn-combo | 84.3 % | intended pairing; fine but hellfire beats it |
| hollow-disruption | **12.5 %** | **best HOLLOW pilot**, barely |

**Affinity: tempo / combo, and possibly aggro.** High `on_curve`
(3.0) + high `tempo_per_aether` (3.0) generalises well across
decks because every deck benefits from hitting curve. The reason
wavebreaker is "too strong" on TC in PairCorrectly isn't its
weights — it's the deck.

**Ready for `stable/` promotion under `combo` affinity**, and
deserves a second affinity tag for `tempo`. The 12.5 concern
("wavebreaker is over-tuned") turns out to be a deck problem, not
a weight problem — pulling wavebreaker back would hurt it on three
decks to moderately fix one.

---

## What this matrix changes about the 12.5 plan

1. **`ember-aggro.suggested_ai` should probably become
   `wavebreaker`**, not `hellfire`. Hellfire's 16.1 % on its own
   deck is worse than wavebreaker's 38.5 %. Either repin, or edit
   hellfire's `default` / `early_tempo` to lift `threat_avoidance`
   off 0.0.
2. **`hollow-disruption` is the real bottleneck.** 9.8 % deck-avg
   decisive WR. No pilot can fix this. Dispatch to
   [`12.6`](../steps/12.6-cross-matchup-polish.md) (tech slots) or
   re-open [`12.3`](../steps/12.3-card-threat-audit.md) for a
   Force-3+ HOLLOW body.
3. **`tide-thorn-combo` is too strong regardless of pilot.** Pulling
   wavebreaker back would hurt 3 decks to moderately fix 1.
   **Pull the deck back** (trim DriftStriker to 2, WaveCount to 1)
   — this is the cheapest intervention.
4. **fortress is the right BC pilot**, but short of the 15 pp
   matched-pair bar. A targeted retune (raise `conduit_softness`
   slightly in `default` to 1.0, lift `tempo_per_aether` off 0.3)
   might close the gap.
5. **Promotion candidates for `stable/`**: fortress (after retune) +
   wavebreaker (now, with `combo` + `tempo` affinity). hellfire +
   reaper need either deck work or repin before promotion is
   justified.
