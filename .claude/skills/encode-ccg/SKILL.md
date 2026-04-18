---
name: encode-ccg
description: Encode an existing CCG into CCGNF. Invoke when the user asks to "encode a game", "port a CCG to ccgnf", "write ccgnf for <game>", "add a new game alongside Resonance", or describes an original CCG and wants it machine-readable. Walks through rules capture, project layout, incremental declaration, validation, and commit.
---

# Encode a CCG into CCGNF

Turn a card game's rules document + card list into a validated CCGNF
encoding under this repository.

## Start here

**Before writing any `.ccgnf`, read [`docs/ccgnf-language.md`](../../../docs/ccgnf-language.md) end-to-end.**
That is the language reference — every form below is defined there. If
you haven't read it this session, read it now.

Also useful for runtime semantics when you hit ambiguities:

- [`grammar/GrammarSpec.md`](../../../grammar/GrammarSpec.md) — engine spec.
- [`docs/plan/reference/builtins.md`](../../../docs/plan/reference/builtins.md) — what the interpreter can actually do today.
- [`encoding/`](../../../encoding/) — the reference game (Resonance). Use as a pattern, not as boilerplate to copy.

## Core contract

- **Encoding follows the rules, not your creativity.** This skill is a
  port. If the target game has a rulebook, every rule in the rulebook
  should map to either a CCGNF construct, a macro, or an explicit
  ruling comment acknowledging what you deferred.
- **The encoding must parse and validate at every commit.** Run the
  validator after every file you add, not once at the end.
- **Prefer macros over copy-paste.** If two abilities differ by one
  number, write one macro and parameterize it.
- **Human-readable cards keep a `// text:` comment** derived from the
  canonical rulebook text. It's the bridge between rulebook and
  encoding during review.
- **No procedures, ever.** If you reach for a top-level `for` loop to
  express a rule, stop. The rule is under-decomposed. Express it as an
  ability with a lambda, a triggered event, or a state-based action.

## Workflow

The steps below are ordered. Do them sequentially. Each step ends with
a checkpoint — validate and commit before moving on.

### 1. Gather inputs from the user

Ask the user (in one round, don't interrogate):

1. **Game name.** Will become the directory prefix under `encoding/`.
2. **Rulebook location.** Path to a markdown doc, a PDF, a link, or an
   in-chat paste. Anything workable.
3. **Card list.** CSV / spreadsheet / markdown / rulebook appendix.
4. **Scope.** Full game? Core set only? A single keyword as proof of
   concept? Be explicit about what ships in this session.
5. **Origin.** Is this an original game (user owns rights) or an
   existing published CCG (IP concerns — encode rules mechanically
   from memory only, don't lift flavor/art text).

If you have enough context from earlier in the conversation, skip
asking and summarize what you're proceeding with so the user can
correct.

### 2. Read and categorize the rules

Skim the rulebook once, then produce a short plan (a paragraph or two,
in chat, not a file) that answers:

- **What entities exist?** At minimum: Game, Player. Beyond that: phases,
  arenas/zones-that-are-entities, conduits, battlefield lanes, etc.
- **What zones does each player own?** (Deck, Hand, Battlefield,
  Graveyard, Exile, resource zones, etc.)
- **What is the turn structure?** List phases in order. Identify which
  are simultaneous vs. sequential.
- **What resources exist?** (Life, Mana, Energy, Experience, …) Each
  is a counter on the Player or a dedicated Entity.
- **What card types / supertypes exist?** (Creature, Spell, Artifact,
  Enchantment, Land, …)
- **What keyword abilities exist?** These become macros.
- **What state-based actions exist?** (Creatures at 0 toughness die,
  players at 0 life lose, etc.) These become `Static` abilities on
  `Game` with `check_at: continuously`.
- **What are the win conditions?** (One-life, mill, alternate wins.)

Write this as a plain message to the user and confirm before moving on.

### 3. Lay out the directory

Create under `encoding/<game-slug>/`:

```
encoding/<game-slug>/
  common/                 ← Level 0 framework primitives, if game-specific ones needed;
                            otherwise reuse encoding/common/ at the root.
  engine/
    00-rulings.ccgnf      ← rulings (empty at first)
    01-resource-macros.ccgnf
    02-keyword-macros.ccgnf
    03-entities.ccgnf     ← Game, Player, phases, zones
    04-setup.ccgnf        ← Triggered on Event.GameStart
    05-turn.ccgnf         ← Triggered on phase-begin events
    06-sba.ccgnf          ← Static abilities for state-based actions
    07-play.ccgnf         ← play chain (casting a card)
    08-combat.ccgnf       ← combat protocol if applicable
  cards/
    <faction|type|color>.ccgnf   ← one file per natural grouping
```

You do **not** need to populate every file. Start with what the current
scope requires; stub others with a header comment if they'll exist
later. Numeric prefixes drive lexical processing order — respect them.

If the user picked "reuse root encoding/common/", skip creating a
sibling `common/`.

### 4. Declare the engine entities first

Always start with Game and Player.

```ccgnf
// engine/03-entities.ccgnf
Entity Game {
  kind: Game
  characteristics: { turn: 1, active_player: Unbound }
  abilities: []     // populated by setup + turn files
}

Entity Player[i] for i ∈ {1, 2} {
  kind: Player
  characteristics: { starting_life: 20 }
  counters:        { life: 20, mana: 0 }
  zones: {
    Deck:        Zone(order: sequential, visibility: face_down_private),
    Hand:        Zone(capacity: 7, order: unordered, visibility: private_to_owner),
    Battlefield: Zone(order: unordered, visibility: public),
    Graveyard:   Zone(order: FIFO, visibility: public)
  }
  abilities: []
}
```

Validate after this file alone:

```bash
dotnet.exe run --project src/Ccgnf.Cli -- encoding/<game-slug>/engine/03-entities.ccgnf
# Expect: "OK: … parsed, built, and validated successfully"
```

Commit: `feat(<game-slug>): declare Game and Player entities`.

### 5. Set up the game

Setup is a single Triggered ability on Game, fired by
`Event.GameStart`:

```ccgnf
// engine/04-setup.ccgnf
Game.abilities += Triggered(
  on:     Event.GameStart,
  effect: SetupSequence())

define SetupSequence = Sequence([
  RandomChoose(value: {Player1, Player2}, bind: Game.active_player),
  ForEach(p ∈ {Player1, Player2}, Shuffle(p.Deck)),
  ForEach(p ∈ {Player1, Player2}, Draw(p, amount: 7)),
  EmitEvent(Event.TurnBegin(player: Game.active_player))
])
```

The details (mulligans, first-player draw adjustments, starting-player
choice) vary per game. Pull them straight from the rulebook; don't
invent.

Validate + commit.

### 6. Encode the turn structure

One Triggered ability per phase, on `Event.PhaseBegin` (or whatever
your phase-begin event is named). Typical shape:

```ccgnf
Game.abilities += Triggered(
  on:     Event.TurnBegin(player=p),
  effect: Sequence([
    Draw(p, 1),
    OpenTimingWindow(main_phase, owner=p),
    DrainTriggersFor(window=main_phase)
  ]))

Game.abilities += Triggered(
  on:     Event.PhaseBegin(phase=CombatStart, player=p),
  effect: BeginCombat(p))
```

When the engine doesn't have a builtin for a game-specific operation
(e.g., `BeginCombat`), write a macro that expands to `Sequence([...])`
of the primitives it does have.

Validate + commit per phase file.

### 7. State-based actions (SBAs)

Every rule that "automatically happens when X" goes here as a
`Static` ability with `check_at: continuously`.

```ccgnf
// "A creature at 0 toughness is destroyed."
Game.abilities += Static(
  modifies: BoardState,
  check_at: continuously,
  rule: ForEach(c -> c.kind == Creature ∧ c.toughness <= 0,
          EmitEvent(Event.Destroy(target: c, reason: lethal))))

// "A player at 0 life loses."
Game.abilities += Static(
  modifies: BoardState,
  check_at: continuously,
  rule: ForEach(p -> p.kind == Player ∧ p.life <= 0,
          EmitEvent(Event.Lose(player: p, reason: "life_depleted"))))
```

Validate + commit.

### 8. Encode keywords as macros

For every keyword in the target game — Flying, Trample, Haste, Reach,
Deathtouch, etc. — write one macro. Parameterized when the keyword
takes a number (Regeneration N, Tribute N).

```ccgnf
// engine/02-keyword-macros.ccgnf
define KeywordFlying = Static(
  modifies: CombatRules,
  rule: self has {flying})

define KeywordTrample = Static(
  modifies: CombatRules,
  rule: self has {trample})

define KeywordLifelink(n) = Triggered(
  on:     Event.Damage(source=self, target=t, amount=a),
  effect: IncCounter(self.controller, life, a * n))
```

Keywords that don't yet have a clean encoding: add a ruling comment
in `00-rulings.ccgnf` describing what you're deferring, and emit
them from cards as a tag only:

```ccgnf
abilities: [ Static(tags += {cascade}) ]
// TODO R-7: Cascade is parsed as a tag; its effect lands when the
// play chain supports look-ahead.
```

Validate + commit.

### 9. Encode cards

Group cards by the natural axis (color/faction/set). One file per
group. For each card:

1. Extract the canonical rulebook text into a `// text:` comment.
2. Translate each clause into:
   - A `type`, `cost`, etc. field.
   - Keywords as macro invocations in `abilities:`.
   - Other abilities as literal `OnResolve(...)` / `Triggered(...)` /
     etc. blocks.

```ccgnf
Card GrizzlyBears {
  factions: {GREEN}
  type: Creature
  subtype: Bear
  cost: 2
  power: 2
  toughness: 2
  rarity: C
  abilities: []
  // text: (vanilla)
}

Card LightningBolt {
  factions: {RED}
  type: Spell
  cost: 1
  rarity: C
  abilities: [
    OnResolve(
      Target(t -> t.kind ∈ {Creature, Player},
        DealDamage(target, 3)))
  ]
  // text: Deal 3 damage to any target.
}
```

Use macros aggressively. When two cards differ by a single number,
one of them should call a parameterized macro defined in
`01-resource-macros.ccgnf` or `02-keyword-macros.ccgnf`.

Validate per file; commit in small batches (5–20 cards per commit).

### 10. Final validation and README

Run the full pipeline + test suite:

```bash
make DOTNET=dotnet.exe ci     # Windows
make ci                       # Linux
```

If any test fails, fix before proceeding. Then:

- Update [README.md](../../../README.md) Status table if a new game
  has been added (there's precedent in the Status table pattern).
- Update [`docs/plan/audit/current.md`](../../../docs/plan/audit/current.md) if you
  changed anything in `src/` to support the encoding.
- Add a devlog entry describing what was encoded and what was
  deferred.

Final commit: `feat(<game-slug>): initial encoding — <N> cards, <scope summary>`.

## When to stop and ask

Stop and ask the user when you hit:

- A rule that can't be expressed without a new engine builtin. Don't
  write the builtin unilaterally; confirm scope with the user first.
- An ambiguity in the rulebook. Propose an interpretation, ask them
  to confirm, and record as a ruling.
- IP concerns: if the game is published and you're encoding from the
  printed rules, confirm the user has rights to maintain this encoding
  in the repo.

Otherwise, proceed steadily and commit often.

## Common pitfalls

- **Don't copy encoding/ files blindly.** Resonance's phase names
  (Rise, Channel, Clash, Fall, Pass) and its `faction`/`aether`/`debt`
  vocabulary are game-specific. Use them as *pattern* examples, not as
  templates to rename.
- **Don't write `Procedure` blocks.** The parser rejects them. Every
  rule is an ability.
- **Don't emit card text as prose fields.** Put rulebook prose in
  `// text:` comments; the rules themselves go in `abilities:`.
- **Don't skip the 2-digit file prefixes.** The engine processes files
  in filename order; numeric prefixes make that order explicit.
- **Don't use macro names that collide with existing builtins.**
  Grep `src/Ccgnf/Interpreter/Builtins.cs` for the current catalog.

## What "done" looks like

A commit that:

- All `.ccgnf` files under `encoding/<game-slug>/` parse and validate.
- `make ci` is green.
- The encoding covers the scope the user asked for (full game, core
  set, or smaller proof-of-concept).
- Deferred items are documented as rulings with R-N identifiers in
  `engine/00-rulings.ccgnf`.
- The README Status table reflects the new game, if it's a first-class
  citizen.

Hand control back to the user with a summary: files created, cards
encoded, rulings deferred, test result.
