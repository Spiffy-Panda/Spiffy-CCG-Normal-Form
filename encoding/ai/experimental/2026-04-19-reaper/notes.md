# Reaper — experimental CPU (2026-04-19, → hollow-disruption)

> **2026-04-20 — Step 12.1 retune (post-12.4 deck reconstruction).**
> `default` and `early_tempo` now lean slightly more on Conduit
> pressure (`tempo_per_aether` 0.8 → 1.5 in default,
> `conduit_softness` 0.5 → 1.0, `lowest_live_hp` 3.5 → 3.0): with
> HOLLOW's new 9-closer deck load, reaper was holding cards in
> `default` and never transitioning to `pushing` fast enough.
> `pushing` itself bumped `conduit_softness` 2.5 → 3.5 and dropped
> `threat_avoidance` 0.8 → 0.5 so the finishing sequence actually
> commits once triggered.
>
> **Bench:** PairCorrectly at baseline seed 1 measured 143/240 draws
> (59.6 %), HolReap 2-53-65; before these edits the same config
> measured 142/240 (59.2 %), HolReap 3-53-64. Net movement ±1 game —
> **noise floor**. Conclusion: HolReap's weakness isn't reaper's
> weights; it's the matchup triangle (HOLLOW can't pressure a
> Sentinel-wall deck without board presence). The 12.2 engine-knob
> queue is the next lever per `12.3` exit criteria. Kept the edits
> because the new tempo values match reaper's stated design (a
> removal-first bot that still commits to its closers once it has
> them) better than the previous tempo 0.8 / cs 0.5 shape.

> **2026-04-20 — Step 12.1 floor edit.** `pushing` intent now satisfies
> the `conduit_softness ≥ threat_avoidance` floor rule
> (`cs: 0.8 → 2.5`, `ta: 1.0 → 0.8`). Removal-first identity preserved
> in `default`, `early_tempo`, and `defend_conduit` (dominant
> `lowest_live_hp` untouched); the edit only asserts that Reaper
> finishes when it has already committed to closing.
>
> **Bench:** `ReapNew` vs `ReapOld` on `hollow-disruption`, 40 games.
> Result `4-11-25` — ReapNew won only 26.7% of 15 decisive games, a
> net **regression** versus pre-edit weights. Notably avg game length
> dropped to 303 steps (from 331 in the 12.0 baseline's HolReap row),
> so the bots *are* engaging `pushing` more often; pre-edit Reaper's
> stall-through-ambiguity behaviour was accidentally winning the
> mirror. **Not promoted to `stable/`.** Kept anyway because the
> floor rule is a hard invariant — a bot that won't close when ahead
> is broken by definition, even if mirror-match numbers prefer the
> broken version. Artifact:
> [`ai-testing-data/12.1-reaper.results.json`](../../../../ai-testing-data/12.1-reaper.results.json).
> Diagnosis: hollow-disruption lacks reliable closers (see the "No
> closer" weakness below) — fix lives in Step 12.3, not here.

## Concept

Reaper is the removal-first profile: it spends every targeted action
on finishing the opponent's weakest live thing. `lowest_live_hp` sits
at 3.5 baseline and climbs to 4.0 under `defend_conduit`, dominating
the `target_entity` scoring curve to the point that Reaper will
almost always chain damage into whichever entity is closest to dying
before considering a fresh target. `conduit_softness` is deliberately
low (0.5 default, 0.2 under `defend_conduit`) — Reaper isn't a closer,
it's a board-cleaner. A playtester's tell: the same wounded opposing
Unit gets killed on the turn it would otherwise have recovered, and
Reaper ignores a 1-integrity Conduit in favour of a 2-HP Unit that's
threatening its board.

## Target deck

`hollow-disruption`. HOLLOW's pilfer/disruption shell already thins
the opponent's best plays; Reaper weaponises that thinning by
slamming every live threat the moment its HP drops, denying recovery
windows.

## Expected tournament result

- **Edges `utility` on `hollow-disruption`** mirror match — the shared
  weight table will break slightly more games for Reaper because
  execution-focus converts "almost dead" into "dead" one turn sooner.
- **Loses to `utility` on `ember-aggro`** — Reaper's low
  `conduit_softness` means it will stand there removing Units while
  an EMBER burn deck races the face clock.
- **Close to `utility` on the slower decks** (`bulwark-control`,
  `tide-thorn-combo`) — still gains on removal but loses some games
  that stall to an inevitable Conduit collapse.

## Known weaknesses

- **No closer**: a near-dead opposing Conduit looks less attractive
  than a 2-HP Unit, so Reaper will clock out games it could end on
  the spot.
- **Over-invests in dying targets**: if two removal actions would
  kill the same 1-HP Unit, Reaper is happy to over-pay. No
  de-duplication consideration exists — weights alone can't solve
  this without a new consideration class, which is out of scope.
- **Against wide boards**: Reaper picks the softest target, not the
  most impactful. A wide HOLLOW board itself can overwhelm Reaper
  because it never prioritises breadth of removal.

## Results

`POST /api/ai/tournament`, `games=4`, `seed=1`, 2000 inputs/50k events
per game. Harness is mirror-match, so rows report bot-vs-self.

| Deck              | profile                               | W / L / D | winRate | avgSteps |
|-------------------|---------------------------------------|-----------|--------:|---------:|
| hollow-disruption | fixed                                 | 0 / 0 / 4 |   0.000 |   438.75 |
| hollow-disruption | utility                               | 0 / 1 / 3 |   0.000 |   360.25 |
| hollow-disruption | experimental/2026-04-19-reaper        | 0 / 1 / 3 |   0.000 |   347.00 |
| ember-aggro       | utility                               | 0 / 0 / 4 |   0.000 |   437.25 |
| ember-aggro       | experimental/2026-04-19-reaper        | 0 / 0 / 4 |   0.000 |   437.25 |

On the target deck `hollow-disruption` Reaper resolves the mirror 13
steps faster than `utility` (347 vs 360) while sharing the W/L/D
pattern — removal-first reaches terminal board states earlier. The
off-target `ember-aggro` row is identical to `utility`, which is the
expected outcome: Reaper's low `conduit_softness` still can't close
an ember-aggro mirror that every profile stalls on at ~437 steps.
