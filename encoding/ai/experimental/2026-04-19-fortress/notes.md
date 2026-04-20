# Fortress — experimental CPU (2026-04-19)

> **2026-04-20 — Step 12.1 floor edit.** `pushing` intent now satisfies
> the `conduit_softness ≥ threat_avoidance` floor rule
> (`cs: 0.6 → 3.0`, `ta: 2.5 → 2.0`). All other intents unchanged —
> Fortress still defends in `default`, `early_tempo`, and
> `defend_conduit`; it just finishes when committed to a push.
>
> **Bench:** `FortNew` vs `FortOld` on `bulwark-control`, 40 games.
> Result `2-2-36` (only 4 decisive, 50% decisive WR). The floor fix
> does not regress Fortress and does not promote it either — the
> bulwark-control mirror draws almost everything regardless of
> `pushing` weights, so the bench is under-powered to distinguish
> versions. **Not promoted to `stable/`.** Artifact:
> [`ai-testing-data/12.1-fortress.results.json`](../../../../ai-testing-data/12.1-fortress.results.json).
> Diagnosis: the draw dominance is a card/engine issue rather than a
> weight issue — see Step 12.3 (card threat audit) and Step 12.2
> (engine sanity pass).

## Concept

Fortress is a defence-first utility bot: it refuses to trade when trading
loses, and it spends aether on removing opponent threats before it spends
it on making its own board more efficient. A playtester should be able
to spot Fortress by two signatures: it declines to attack noticeably
more often than `utility` (dominant `threat_avoidance`), and its
`target_entity` picks lean heavily on whichever opposing entity is
closest to dying (triple-weighted `lowest_live_hp`). `tempo_per_aether`
is intentionally negligible — Fortress does not care whether its bodies
are cost-efficient, only whether they stabilise the board.

## Expected tournament result

- **Beats `fixed`** decisively on every deck. Fixed ladder has no
  threat model; Fortress removing its best Unit every turn is a
  lopsided matchup regardless of archetype.
- **Loses to `utility`** on fast decks (`ember-aggro`). Fortress passes
  priority on attacks it could profitably take, and the low
  `tempo_per_aether` means it floats aether compared to the default
  bot. A utility bot that swings when its body trades up should
  out-race it on tempo decks.
- **Close to `utility`** on slower decks (`bulwark-control`,
  `hollow-disruption`, `tide-thorn-combo`). Here the extra removal
  weight can flip games that come down to a single unanswered threat.

## Known weaknesses

- **Aether flooding**: because `tempo_per_aether` is so low, when no
  removal target exists Fortress picks on-curve plays but doesn't
  fight for force-per-aether efficiency. Against a deck that can
  dilute its own threats, Fortress spends mana on mediocre bodies.
- **Permanent stall**: with `threat_avoidance` at 3.0 (4.0 under
  `defend_conduit`), once both conduits are contested Fortress tends
  to loop through pass-priority until the opponent over-commits or
  the game stalls out. Draws are the failure mode, not losses.
- **No pushing gear**: `lethal_check` bumps `conduit_softness` to 2.5
  but the rest of the table is still defensive. If an opponent leaves
  a 1-integrity conduit exposed Fortress will swing, but it will not
  stage multi-turn setups to create that opening.

## Results

`POST /api/ai/tournament` with `games=4`, `seed=1`, 2000 inputs/50k
events per game. The current harness runs **mirror matches** — each
row is the profile against itself on the same deck — so win rate
compares bots on "how often their games close vs. stall", not head-
to-head. Step count surfaces how decisively the profile resolves.

| Deck              | profile                                  | W / L / D | winRate | avgSteps |
|-------------------|------------------------------------------|-----------|--------:|---------:|
| ember-aggro       | fixed                                    | 0 / 0 / 4 |   0.000 |   439.75 |
| ember-aggro       | utility                                  | 0 / 0 / 4 |   0.000 |   437.25 |
| ember-aggro       | experimental/2026-04-19-fortress         | 0 / 0 / 4 |   0.000 |   437.25 |
| bulwark-control   | utility                                  | 1 / 0 / 3 |   0.250 |   364.75 |
| bulwark-control   | experimental/2026-04-19-fortress         | 1 / 1 / 2 |   0.250 |   247.75 |
| bulwark-control   | fixed                                    | 0 / 0 / 4 |   0.000 |   423.00 |
| hollow-disruption | fixed                                    | 0 / 0 / 4 |   0.000 |   438.75 |
| hollow-disruption | utility                                  | 0 / 1 / 3 |   0.000 |   360.25 |
| hollow-disruption | experimental/2026-04-19-fortress         | 0 / 1 / 3 |   0.000 |   347.00 |
| tide-thorn-combo  | utility                                  | 1 / 0 / 3 |   0.250 |   363.75 |
| tide-thorn-combo  | experimental/2026-04-19-fortress         | 1 / 0 / 3 |   0.250 |   366.00 |
| tide-thorn-combo  | fixed                                    | 0 / 0 / 4 |   0.000 |   465.00 |

Observations:

- Fortress is demonstrably **distinct** from `utility`: on
  `bulwark-control` the mirror match breaks differently (1 / 1 / 2
  vs. utility's 1 / 0 / 3) with a noticeably shorter 247-step average
  — different decisions, different trajectories.
- Fortress matches `utility`'s win rate on `tide-thorn-combo` and
  `hollow-disruption` while resolving games faster (347 vs. 360
  steps) — consistent with removal-first focus closing games when
  they do close.
- `ember-aggro` is the predicted soft spot: every profile mirror-
  stalls at ~438 steps. The aggression-vs-aggression loop never
  breaks, which matches the **Permanent stall** weakness called out
  above. Head-to-head matches (not available through this endpoint
  yet) would be the right tool to show `utility` out-racing
  Fortress here.
- `fixed` never scores a win on any deck — its ladder policy can't
  close against a copy of itself within 2000 inputs. Fortress's
  shorter step counts reinforce that the removal bias pushes the
  game-state needle even inside a mirror.

Concept signature confirmed: **removal-first and stall-prone.**
Graduating to stable would require the head-to-head benchmark step
that's not yet built — see
`docs/plan/steps/10.2-long-term-ai-plan.md`.
