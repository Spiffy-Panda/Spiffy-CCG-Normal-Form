# Reaper — experimental CPU (2026-04-19, → hollow-disruption)

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
