# encoding/

Machine-readable CCGNF source for Resonance. Human-readable rules and card lists live under `../design/`.

## Layout

```
common/       Framework primitives. No game-specific references.
  00-conventions.ccgnf    Syntax rules, forbidden constructs.
  01-schema.ccgnf         The six primitives; characteristic layering.
  02-lifecycle.ccgnf      Ability kinds, timing windows, once_per_turn.
  03-combinators.ccgnf    Sequence, ForEach, If, Cond, Choice, Target, let.

engine/       Resonance game engine.
  00-rulings.ccgnf            R-1..R-6 — authoritative design decisions.
  01-resonance-macros.ccgnf   CountEcho, Resonance, Peak, Banner, Tiers.
  02-trigger-shorthands.ccgnf OnEnter, StartOfYourTurn, OnCardPlayed, etc.
  03-keyword-macros.ccgnf     Surge, Blitz, Fortify, Mend, Sentinel, Drift,
                              Recur, Reshape, Rally, Sprawl, Kindle,
                              Phantom, Shroud, Pilfer, DeploymentSickness,
                              Interrupt, Unique.
  04-entities.ccgnf           Game, Arena, Player, Conduit, PlayBinding.
  05-setup.ccgnf              Setup as a Triggered ability on Game.
  06-turn.ccgnf               Rise, Channel, Clash, Fall, Pass phases.
  07-sba.ccgnf                State-based actions.
  08-play.ccgnf               PlayCard event chain; next_rise_refresh.
  09-clash.ccgnf              Clash as declarative derived values.
  10-tokens.ccgnf             ThornSapling template.

cards/        Card definitions, one file per faction.
  ember.ccgnf    bulwark.ccgnf    tide.ccgnf    thorn.ccgnf    hollow.ccgnf
  dual.ccgnf     neutral.ccgnf

DESIGN-NOTES.md    Non-code notes: what fell out cleanly, fixed bugs,
                   remaining subtleties, deferred schema choices.
```

## Processing order

The grammar engine (see `../grammar/GrammarSpec.md`) processes files in a level-based order:

1. `common/` — no references "up" the graph.
2. `engine/` — may reference `common/`.
3. `cards/` and `engine/10-tokens.ccgnf` — may reference `common/` and `engine/`.

Within a level, files are processed in lexical (filename) order, enforced by the numeric prefixes.

## File format

- Extension: `.ccgnf` (Cardgame Normal Form).
- Comments: `//` line comments, `/* ... */` block comments.
- Unicode operators (`∈`, `∧`, `∨`, `¬`, `×`, `⊆`) are accepted alongside their ASCII equivalents (`in`, `and`, `or`, `not`, `x`, `subseteq`).
- Each file is a top-level sequence of declarations: entity, card, token, macro, or entity augmentation.
- No `Procedure` blocks. No top-level imperative statements.

## Editing

When changing rules:

1. Update `../design/GameRules.md` (the human spec).
2. Mirror the change here in the relevant `engine/` or `cards/` file.
3. If the change resolves an existing Ruling (R-1 through R-6), remove the ruling from `engine/00-rulings.ccgnf`.
4. If the change introduces a new ambiguity, add a new ruling.

Commits should touch either `design/` or `encoding/` but not both in the same commit unless the change is trivially mechanical. This keeps the history legible.
