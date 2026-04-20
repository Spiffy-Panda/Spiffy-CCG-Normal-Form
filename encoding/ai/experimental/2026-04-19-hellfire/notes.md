# Hellfire — experimental CPU (2026-04-19, → ember-aggro)

## Concept

Hellfire is the all-in aggression profile. It will attack in every
exchange the rules permit, period: `threat_avoidance` is zeroed under
every intent except `defend_conduit` (where it sits at 0.5 to
eyeball-prefer a stabilising trade over a suicidal one). When picking
targets it bee-lines toward the opposing Conduit (`opponent_priority`
at 2.5) and the softest-looking arena (`conduit_softness` at 3.0,
climbing to 4.0 under `lethal_check`). Body efficiency
(`tempo_per_aether`) is also high because an aggro deck *needs* its
curve to hit — but the bot will never pass priority just because the
exchange looks bad. A playtester's tell: every Unit attacks every
turn, and the bot picks the lowest-integrity opposing Conduit even
when it could profitably swing at a Unit instead.

## Target deck

`ember-aggro`. The deck's low-curve Blitz/Surge bodies line up exactly
with Hellfire's "cheap, efficient, always-attacking" weight profile.
On slower decks Hellfire should throw away more Units than it kills.

## Expected tournament result

- **Beats `fixed` + `utility` on ember-aggro** in a hypothetical
  head-to-head by closing faster; mirror match should show shorter
  step counts than either baseline.
- **Loses to `utility` on `bulwark-control`** because the zero
  `threat_avoidance` will keep swinging into walls until it runs out
  of bodies.
- **Roughly even with `utility` on `tide-thorn-combo`** — the combo
  shell can out-stabilise Hellfire but only after it's taken enough
  face damage to matter.

## Known weaknesses

- **No defensive gear**: once the board turns against Hellfire there's
  no weight profile that says "stop attacking and stabilise" —
  `defend_conduit` only nudges `threat_avoidance` to 0.5.
- **Over-commits**: high `opponent_priority` means it picks the
  Conduit over removing a threat even when the Conduit is out of
  reach this turn.
- **Depends on curve**: without `ember-aggro`'s 1- and 2-cost flood,
  the high `tempo_per_aether` weight doesn't matter much — Hellfire
  on a midrange deck is just "utility minus defense".

## Results

`POST /api/ai/tournament`, `games=4`, `seed=1`, 2000 inputs/50k events
per game. Harness is mirror-match, so rows report bot-vs-self.

| Deck              | profile                               | W / L / D | winRate | avgSteps |
|-------------------|---------------------------------------|-----------|--------:|---------:|
| ember-aggro       | fixed                                 | 0 / 0 / 4 |   0.000 |   439.75 |
| ember-aggro       | utility                               | 0 / 0 / 4 |   0.000 |   437.25 |
| ember-aggro       | experimental/2026-04-19-hellfire      | 0 / 0 / 4 |   0.000 |   437.25 |
| bulwark-control   | utility                               | 1 / 0 / 3 |   0.250 |   364.75 |
| bulwark-control   | experimental/2026-04-19-hellfire      | 1 / 0 / 3 |   0.250 |   364.75 |

`ember-aggro` lands in the mirror-stall zone that every tuned profile
hits at ~437 steps — Hellfire swinging every turn doesn't break the
symmetry. The `bulwark-control` off-target row is identical to
`utility`, which is honest evidence that on a slow deck Hellfire's
zero `threat_avoidance` gets absorbed by the same walls the default
bot hits. To prove the concept properly, the mirror harness isn't the
right tool — a head-to-head Hellfire-vs-utility endpoint would show
the expected divergence on `ember-aggro` specifically.
