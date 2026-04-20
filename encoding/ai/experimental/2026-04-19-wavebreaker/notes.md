# Wavebreaker — experimental CPU (2026-04-19, → tide-thorn-combo)

## Concept

Wavebreaker is the pure-efficiency profile: hit your curve, spend
aether on the biggest body per mana, repeat. `on_curve` and
`tempo_per_aether` both sit at 3.0 under `default` and stay elevated
across every intent, meaning the bot *will* pass on a play that
doesn't match its curve or doesn't return enough force. It doesn't
try to race (`conduit_softness` low) and isn't a removal machine
(`lowest_live_hp` at 1.2). Instead it relies on out-tempo-ing the
opponent with a sequence of efficient bodies. A playtester's tell:
Wavebreaker never plays a card that doesn't exactly match its current
aether unless it has no on-curve option, and when picking between two
cards it always chooses the one with the higher force-per-cost.

## Target deck

`tide-thorn-combo`. TIDE+THORN's sticky low-cost bodies and shift
tech reward a bot that reliably spends every point of aether on
efficient stats instead of chasing removal. Wavebreaker on Brinescribe
+ Thornling + Bramblepup should stabilise boards by pure stat
efficiency.

## Expected tournament result

- **Slightly ahead of `utility` on `tide-thorn-combo`** mirror —
  the deck's curve is dense enough that the on-curve bias converts
  more games than it wastes.
- **Loses to `utility` on `ember-aggro`** because refusing off-curve
  plays forfeits trades that would stabilise against burn.
- **Rough parity with `utility`** on the control decks — Wavebreaker
  trades tempo for tempo cleanly but doesn't bring Reaper's removal
  bite or Fortress's defensive weight.

## Known weaknesses

- **Refuses off-curve plays**: if the on-curve option is garbage,
  Wavebreaker will still pick it over a two-cost pivot. The
  consideration curve means "cost diff of 1" already halves the
  score, so a 3-cost card with aether=4 competes with a 4-cost
  card at roughly double the score — even if the 3-cost is a dead
  card in hand.
- **No late-game gear**: `tempo_per_aether` caps at force/cost≈4,
  so an 8/8 for 4 maxes out the same as a 4/1. Wavebreaker can't
  prefer high-impact bombs over merely efficient bodies.
- **Can't close**: `conduit_softness` low means Wavebreaker stays
  in a tempo race long after it should have committed to closing.
  Games resolved by stat pressure; games stalled by face-damage
  indecision.

## Results

`POST /api/ai/tournament`, `games=4`, `seed=1`, 2000 inputs/50k events
per game. Harness is mirror-match, so rows report bot-vs-self.

| Deck              | profile                                  | W / L / D | winRate | avgSteps |
|-------------------|------------------------------------------|-----------|--------:|---------:|
| tide-thorn-combo  | utility                                  | 1 / 0 / 3 |   0.250 |   363.75 |
| tide-thorn-combo  | experimental/2026-04-19-wavebreaker      | 1 / 0 / 3 |   0.250 |   366.00 |
| tide-thorn-combo  | fixed                                    | 0 / 0 / 4 |   0.000 |   465.00 |
| ember-aggro       | utility                                  | 0 / 0 / 4 |   0.000 |   437.25 |
| ember-aggro       | experimental/2026-04-19-wavebreaker      | 0 / 0 / 4 |   0.000 |   437.25 |

On target `tide-thorn-combo` Wavebreaker matches `utility` in
W/L/D at +2 steps — the tight curve + high-efficiency bias is
indistinguishable from the default weights in a mirror because both
sides over-tempo each other symmetrically. Off-target
`ember-aggro` sits on the universal ~437-step stall plateau. Like
Hellfire, Wavebreaker needs a head-to-head harness to show its
strengths — a self-mirror can't reveal "I out-tempo you" when both
copies know the same plays.
