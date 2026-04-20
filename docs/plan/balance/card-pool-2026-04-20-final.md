# Step 12.3 / 12.4 / 12.1 — post-arc re-audit (2026-04-20)

**Verdict: card pool hits every closer-ratio target; decks are honest;
AI retune cannot unstick the remaining draws.** The 12.3 card
authoring, 12.4 deck reconstruction, and 12.1 retune landed in that
order. Post-arc bench: PairCorrectly draw rate **59.6 %** (from
81.7 % baseline, −22.1 pp). Still above the ≤ 40 % exit target, so
the 12.2 engine-knob queue fires next.

This note supersedes
[`card-pool-2026-04-20-postauthor.md`](card-pool-2026-04-20-postauthor.md)
for the "current state" reading; prior audit note is preserved as
the post-authoring snapshot (before decks used the new cards
honestly).

## Per-faction pool stats (unchanged from -postauthor)

All 134 cards tagged; no new cards authored in 12.4 or this re-audit.

| Faction  | Closer | Setup | Disruption | Filler | Total | Closer % | Target | Hit? |
|----------|-------:|------:|-----------:|-------:|------:|---------:|-------:|:----:|
| EMBER    | 20     | 2     | 0          | 2      | 24    | 83 %     | ≥ 50 % |  ✅  |
| BULWARK  | 7      | 0     | 15         | 0      | 22    | 32 %     | ≥ 30 % |  ✅  |
| TIDE     | 5      | 15    | 3          | 0      | 23    | 22 %     | ≥ 20 % |  ✅  |
| THORN    | 5      | 17    | 1          | 0      | 23    | 22 %     | ≥ 20 % |  ✅  |
| HOLLOW   | 5      | 2     | 16         | 0      | 23    | 22 %     | ≥ 20 % |  ✅  |

## Deck shapes (12.4 reconstruction, final)

All four reference decks rewritten to consume the 12.3 pool honestly:

- **bulwark-control** — 15 defensive (Sentinel / Fortify / Mend /
  Prevent) + 11 closer + 2 Mend-engine. Closer % = 36.7 %.
  `archetypes: [control, midrange]`, `suggested_ai: fortress`.
- **hollow-disruption** — 9 Phantom/Shroud bodies + 12 Pilfer/removal
  disruption + 9 closer. Closer % = 30 %.
  `archetypes: [disruption, combo]`, `suggested_ai: reaper`.
- **tide-thorn-combo** — 10 Rally/Drift 1-drops + 4 TIDE setup + 4
  THORN setup + 9 TIDE closer + 3 THORN closer + 2 Refract bounce.
  Closer % = 40 %.
  `archetypes: [combo, tempo]`, `suggested_ai: wavebreaker`.
- **ember-aggro** — Unchanged from 12.0 (EMBER already at 83 %
  closer, deck was honest).
  `archetypes: [aggro, tempo]`, `suggested_ai: hellfire`.

## AI retune (step 12.1 iteration point)

Two bots touched; both kept at experimental. No promotions to
`stable/` — neither cleared the ≥ 55 % decisive-WR bar against
pre-edit weights.

- **reaper** — `default` / `early_tempo` / `pushing` all tightened
  toward Conduit pressure (`tempo_per_aether` lifted from 0.8 → 1.5
  in default, `conduit_softness` bumped throughout, `pushing`
  deepened). Net bench movement: HolReap 3 wins → 2 wins, draws
  +1 — noise floor.
- **hellfire** — `default` / `early_tempo` tightened on
  `conduit_softness` + `tempo_per_aether`, `threat_avoidance` kept
  at 0. Net bench movement: EmbHell 12 wins → 12 wins — no change.

Kept the edits anyway because they match each bot's stated design
better than the pre-retune tempo shape. Full diagnosis in each bot's
`notes.md`.

## Bench arc (cumulative)

PairCorrectly draw rate, baseline seed 1, 240 games:

| Milestone                                | Draws    | %       | Δ baseline |
|------------------------------------------|---------:|--------:|-----------:|
| 12.0 baseline                            | 196/240  | 81.7 %  |     0      |
| 12.1 floor-fix only                      | 196/240  | 81.7 %  |     0      |
| 12.3 BULWARK cards                       | 190/240  | 79.2 %  |   −2.5 pp  |
| 12.3 HOLLOW cards (mid-pass)             | 189/240  | 78.8 %  |   −2.9 pp  |
| 12.3 TIDE cards                          | 174/240  | 72.5 %  |   −9.2 pp  |
| 12.3 THORN cards (all 19 cards landed)   | 166/240  | 69.2 %  |  −12.5 pp  |
| 12.4 deck reconstruction                 | 142/240  | 59.2 %  |  −22.5 pp  |
| 12.1 retune (post-12.4)                  | 143/240  | 59.6 %  |  −22.1 pp  |
| **Target**                               |  ≤ 96/240 | ≤ 40 % |  ≥ −41.7 pp |

Decisive-WR per pair at the final checkpoint:

| Pair     | 12.0 Baseline | Post-12.3 | Post-12.4 | Post-12.1 | Δ baseline |
|----------|--------------:|----------:|----------:|----------:|-----------:|
| BulFort  | 76.2 %        | 34.5 %    | 52.4 %    | 52.4 %    |  −23.8 pp  |
| HolReap  |  8.1 %        | 14.0 %    |  5.4 %    |  3.6 %    |   −4.5 pp  |
| EmbHell  | 63.6 %        | 30.4 %    | 37.5 %    | 38.7 %    |  −24.9 pp  |
| TiThWave | 94.7 %        | 96.2 %    | 92.4 %    | 92.4 %    |   −2.3 pp  |

SimpleSweep (bulwark-control piloted by every bot, baseline seed,
840 games):

| Milestone                                | Draws    | %       |
|------------------------------------------|---------:|--------:|
| 12.0 baseline                            | 659/840  | 78.5 %  |
| 12.4 + 12.1 (this re-audit)              | 771/840  | 91.8 %  |

SimpleSweep moved the wrong way — the post-12.4 bulwark-control deck
is harder for *every* bot to win with, including fortress. Decisive
WR ranking on the deck: Wave 61.3 % > Fort 58.8 % > Fixed 60 % ≈
Reap 42.8 % > Util 40 % > Hell 6.7 %. That violates the 12.4 "matched
AI wins matched deck by ≥ 15 pp on decisive games" contract —
fortress isn't cleanly the right match for the post-12.3 card pool
anymore. Either the deck archetype needs re-declaration (perhaps
`midrange` alone, not `control`) or a different stable bot fits
better. Flagged for 12.5 (matched-AI tune).

## Exit criteria status

- [x] Every card carries exactly one of the four tags (134 / 134).
- [x] Every mono-faction meets target closer %
      (EMBER 83 %, BULWARK 32 %, HOLLOW / TIDE / THORN 22 % each).
- [x] Every reference deck declares honest `archetypes` and pinned
      `suggested_ai`.
- [x] `card-cluster` SKILL.md role-bucket awareness landed in commit
      e953343.
- [ ] Draw rate ≤ 40 % — **missed at 59.6 %.**
      12.2 engine knobs dispatched.
- [ ] Matched AI beats mismatched AI by ≥ 15 pp on its own deck.
      **Missed for bulwark-control + fortress.** Flagged for 12.5.
- [ ] Every archetype ≤ 6-turn lethal — not verified; deck-level
      question, wait until 12.2 engine changes land and re-bench.

## Draw-rate diagnostic — where the 143 draws sit

| Matchup (40 games)       | Draws | Note                                              |
|--------------------------|------:|---------------------------------------------------|
| BulFort vs EmbHell       | 37    | **BULWARK Fortify soaks EMBER burn**. Primary stall cell. |
| EmbHell vs HolReap       | 30    | Both decks have finish-or-stall patterns, not a midgame. |
| BulFort vs HolReap       | 22    | HOLLOW has no board presence to threaten BULWARK walls. |
| BulFort vs TiThWave      | 19    | TIDE's Reshape out-tempos BULWARK only sometimes. |
| EmbHell vs TiThWave      | 22    | EMBER loses unless it closes in ≤ 4 turns.        |
| HolReap vs TiThWave      | 13    | The closest thing to a decisive matchup we have.  |

The BulFort-EmbHell 37-draw cell is the single biggest drag. Root
cause: Fortify (adds Ramparts while Conduit ≥ 4 integrity) + Sentinel
(Unit Force → Fortification) absorb EMBER's Force projection, and
Ramparts heal at End of Fall — there's no "chip damage" pathway that
sticks. Candidate 12.2 knobs:

1. **Cap Fortify / Ramparts healing per turn.** Stop the Conduit
   integrity from fully restoring each Fall.
2. **Introduce an absolute turn timer.** Hard draw at turn 25
   transitions to tiebreaker (lowest-Conduit-integrity loses).
3. **Arena collapse on repeated zero-damage turns.** If an Arena has
   no incoming damage for 3 turns in a row, the Conduit takes 1
   attrition damage.

## Follow-ups dispatched

1. **12.2 engine knobs** — primary lever for the remaining 19.6 pp of
   draw rate. See diagnostic above for the top 3 candidate knobs.
2. **12.5 matched-AI tune** — fortress may no longer be the best
   match for post-12.3 bulwark-control; SimpleSweep shows fortress
   and wavebreaker ~tied on the deck. Triangulate with a deck-level
   re-pairing bench before 12.5 opens.
3. **Card-pool pass 2 (optional, 12.3 re-opener)** — BulFort-EmbHell
   stall might partly resolve if BULWARK gets an archetype-respecting
   *Sentinel-breaker* card in EMBER (e.g., "Deal X damage that cannot
   be absorbed by Fortification"). Out of scope for the current
   step-12 arc; revisit only if 12.2 knobs don't close the gap.

## Appendix — final artifact paths

- Pre-authoring audit: [`card-pool-2026-04-20.md`](card-pool-2026-04-20.md)
- Post-authoring audit: [`card-pool-2026-04-20-postauthor.md`](card-pool-2026-04-20-postauthor.md)
- Post-12.4 decks bench: `ai-testing-data/12.4-decks.PairCorrectly.results.json`
- Post-12.1 retune bench: `ai-testing-data/12.1-retune.PairCorrectly.results.json`
- Post-12.4 SimpleSweep: `ai-testing-data/12.4-final.SimpleSweep.results.json`
- Per-faction commits (card authoring): `d0955be`, `e5d3aa4`,
  `80ae1e4`, `45ee076`; wrap-up `e953343`; decks `d0c5c6b`.
