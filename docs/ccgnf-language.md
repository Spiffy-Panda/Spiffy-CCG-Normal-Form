# CCGNF — language guide for game authors

Cardgame Normal Form (CCGNF) is a declarative DSL for encoding the
rules and cards of a collectible card game. This document is the
**author-facing reference** — what you write in `.ccgnf` files, what it
means, and the primitives every game reuses.

This guide is game-agnostic. A reference game (Resonance) ships in
[`encoding/`](../encoding/), but every construct below is reusable by any
CCG that fits CCGNF's model.

Companion documents:

- [`grammar/GrammarSpec.md`](../grammar/GrammarSpec.md) — the engine
  specification (what implementers must build).
- [`docs/plan/reference/ast-nodes.md`](plan/reference/ast-nodes.md) — AST record shapes for tooling authors.
- [`docs/plan/reference/builtins.md`](plan/reference/builtins.md) — the v1 interpreter builtin catalog.

---

## 1. Mental model

A CCGNF program describes a game as a **tree of entities**, each
carrying **characteristics**, **counters**, **tags**, **zones**, and
**abilities**. All rules — phase transitions, state-based actions,
card text, keywords — are expressed as abilities attached to entities.
No top-level imperative code; no procedures. The engine drives the
game by dispatching **events** to abilities that match them.

Every construct reduces to six primitives:

| Primitive | What it is |
|-----------|-----------|
| **Entity** | A bundle of state with identity (Game, Player, Card-in-zone, Token, Phase, …). |
| **Zone** | An ordered or unordered bag of entities (Deck, Hand, Battlefield, …). |
| **Event** | A typed, timestamped record (`Event.TurnBegin(player=p)`). |
| **Effect** | A game-state transition (`DealDamage(target, 3)`). |
| **Ability** | An `(kind, trigger, guard, cost, effect)` tuple attached to an entity. |
| **Predicate/Selector** | A boolean-valued expression, typically a lambda (`x -> x.cost <= 3`). |

If your game fits that model, it fits CCGNF.

---

## 2. File organization

### 2.1 Extension

Source files use `.ccgnf`. Both human-readable and machine-parseable —
`//` and `/* … */` are comments; whitespace is insignificant except
where required by the preprocessor (see §3.3).

### 2.2 Levels

A CCGNF **project** is a directory tree. The engine walks it in
dependency-order:

| Level | Purpose | Typical location |
|-------|---------|------------------|
| **0 — common** | Framework primitives reusable across any game. | `encoding/common/*.ccgnf` |
| **1 — engine** | This game's engine: entities, phases, SBAs, keyword definitions. | `encoding/engine/*.ccgnf` |
| **2 — cards** | Card and token declarations for the reference set. | `encoding/cards/*.ccgnf`, `encoding/engine/*tokens*.ccgnf` |

Files within a level are processed in **lexical filename order** (hence
the `00-`, `01-`, `02-` prefixes you'll see). A card file may reference
anything in Levels 0 and 1; a common file may not reference anything
game-specific. This is an author convention; the engine flags
up-level references as errors.

### 2.3 Multi-game projects

Nothing stops you from maintaining multiple games side by side. The
usual shape is:

```
encoding/
  common/           Level 0 — shared by both games
  gameA/
    engine/
    cards/
  gameB/
    engine/
    cards/
```

Point the engine at whichever root you want.

---

## 3. Preprocessor

Before parsing, the preprocessor expands `define` directives. This is
the macro system; use it to compress repetition.

### 3.1 Defining a macro

```ccgnf
define KeywordFlying = Static(
  modifies: CombatRules,
  rule: self has {flying})

define DealDamage(target, amount) =
  Atomic.DealDamage(target, amount)

define Tiers(cases) =
  ChooseHighest(cases)
```

- **Zero-arg:** `define NAME = body` — invoke as bare `NAME`.
- **Parameterized:** `define NAME(x, y, …) = body` — invoke as `NAME(arg1, arg2, …)`.

Bodies may span multiple lines; a body ends at EOF, at a blank line at
bracket depth 0, or at another top-level starter (`define` / `Entity`
/ `Card` / `Token`).

### 3.2 Expansion semantics

- **Token-level substitution.** Not string replacement; the parser
  sees proper tokens.
- **Recursive.** Macros may invoke other macros; expansion iterates
  until a fixed point. Self-reference (direct or cyclic) is an error.
- **No hygiene.** Macro-introduced identifiers can shadow caller-scope
  names; this is a deliberate simplification. Prefix internal names
  you want invisible.

### 3.3 Two guard rules worth memorizing

Identifiers are **not** expanded in two positions:

- **Label position:** followed by `:`. Fields, named args, and switch
  case keys keep their literal text. `{ type: Unit }` is safe even if
  `type` happens to be a macro name.
- **Member position:** preceded by `.`. `Event.Destroy(…)` is a member
  on the `Event` host; the `Destroy` macro does not expand here.

### 3.4 Invocation forms

| Form | Expansion |
|------|-----------|
| `FOO` (zero-arg macro `FOO`) | body |
| `FOO()` (zero-arg macro `FOO`) | body, then the trailing `()` — left in place for the parser |
| `FOO(x)` (one-arg macro `FOO`) | body with `x` substituted |

The `FOO()` form (zero-arg with parens) is tolerated; the parser
evaluates the no-arg call on whatever the body produced. Prefer the
bare form when you can.

---

## 4. Top-level declarations

Every non-macro line at file top level is one of:

### 4.1 Entity

A bundle of state with identity.

```ccgnf
Entity Game {
  kind: Game
  characteristics: { turn: 1, active_player: Unbound }
  abilities: []   // populated later via augmentations
}
```

**Parameterized entities:**

```ccgnf
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

- `[i]` declares one or more **index parameters**.
- `for i ∈ S` (the **for-clause**) instantiates one entity per element
  of `S`. Access them by concatenating the base name with the index
  value: `Player1`, `Player2`.

**Templates:** an entity with `[…]` index params but **no** `for`-clause
is a *template*. It isn't instantiated at setup; runtime effects
create instances with `InstantiateEntity(kind: X, …)`.

**Ephemeral entities:** add `lifetime: ephemeral` to skip setup
instantiation entirely. Useful for temporary bindings (the per-play
state object while a card resolves).

### 4.2 Card

```ccgnf
Card LightningBolt {
  factions: {RED},
  type: Spell,
  cost: 1,
  rarity: C
  abilities: [
    OnResolve(
      Target(t -> t.kind ∈ {Creature, Player},
        DealDamage(target, 3)))
  ]
  // text: Deal 3 damage to any target.
}
```

Cards are entities with a dedicated keyword. They live in the deck/hand
zones during play and resolve via `OnResolve` abilities.

### 4.3 Token

Like `Card`, but typed distinctly for tooling (token templates are
never shuffled into decks; they're summoned by effects).

```ccgnf
Token Spirit {
  characteristics: { name: "Spirit" }
  type: Creature
  power: 1
  toughness: 1
  abilities: [ KeywordFlying ]
}
```

### 4.4 Entity augmentation

A `+=` on a dotted path attaches a value to an existing entity's list
field. The principal use is appending abilities:

```ccgnf
Game.abilities += Triggered(
  on:     Event.GameStart,
  effect: SetupSequence())

Game.abilities += Static(
  modifies: BoardState,
  check_at: continuously,
  rule: ForEach(c -> c.kind == Creature ∧ c.toughness <= 0,
          EmitEvent(Event.Destroy(target: c, reason: lethal))))
```

`Player.abilities += Activated(...)` attaches an ability to all
instances of a parameterized entity. Augmentations target the
template, not a specific instance.

---

## 5. Field syntax

Inside an entity / card / token body, each field is
`key: value` or `key: { nested }`. Value can be any expression.

### 5.1 Common fields (reserved names)

| Field | Expected value | Meaning |
|-------|----------------|---------|
| `kind` | Identifier | The entity's kind — `Game`, `Player`, `Card`, `Creature`, `Token`, etc. |
| `characteristics` | `{ name: value, … }` | Durable named values. Think of them as properties. |
| `counters` | `{ name: int, … }` | Integer counters that effects mutate. |
| `zones` | `{ name: Zone(…), … }` | The entity's owned zones. |
| `tags` | Set literal | Arbitrary markers. |
| `abilities` | List of ability expressions | Attached abilities. Usually `[]` here and populated via augmentation. |
| `owner` / `controller` | Entity reference | For entities created at runtime. |
| `lifetime` | `ephemeral` or `permanent` | Affects setup instantiation. |
| `derived` | `{ name: expr, … }` | Computed characteristics (see §5.3). |

Fields not listed above are stored as characteristics by default.

### 5.2 Zones

```ccgnf
Zone(
  order:       unordered | sequential | FIFO | LIFO,
  capacity:    int | unlimited,
  visibility:  public | private_to_owner | face_down_private | face_down,
  on_overflow: reject | EvictLeftmost | EvictOldest,
  adjacency:   <expression>    // optional; relates to other zones
)
```

All args are named. Only `order` is required in v1; other args have
sensible defaults.

### 5.3 Derived characteristics

Use `typedExpr` form `NAME = expr` inside `derived` to declare a field
that recomputes on each read:

```ccgnf
derived: {
  threat = self.power + Count(self.modifiers),
  is_lethal = self.damage_dealt >= target.toughness
}
```

The engine reads derived fields lazily; the value is never cached
across events.

### 5.4 State flags

State flags are boolean characteristics commonly used for "this arena
has collapsed", "this Creature has summoning sickness", etc.
Declare as an ordinary characteristic of type boolean:

```ccgnf
state_flags: {
  tapped[Player1]: false,
  tapped[Player2]: false
}
```

Indexed-key form lets you key flags by entity reference.

---

## 6. Expression syntax

CCGNF expressions are familiar:

### 6.1 Literals

- **Integer:** `42`, `-7`.
- **String:** `"hello"`.
- **Identifier:** `Rise`, `Player1`, `x`. Resolved via the runtime's
  scope chain (§9).
- **Keyword literals:** `true`, `false`, `None`, `Unbound`, `NoOp`,
  `Default` (the last two are sentinels; see §10).

### 6.2 Collections

- **Set:** `{1, 2, 3}`, `{EMBER, BULWARK}`, `{}`.
- **List:** `[1, 2, 3]`, `[]`.
- **Range:** `[1..5]` = list `[1, 2, 3, 4, 5]`.
- **Tuple:** `(1, 2)`. Use parens; single-element parens are just
  grouping (`(x)` ≡ `x`).
- **Block / map:** `{ key1: v1, key2: v2 }` — structural, not iterable.

`{}` versus block ambiguity resolves by content: fields present → block;
bare expressions → set.

### 6.3 Operators

Tightest to loosest precedence:

| Level | Operators |
|-------|-----------|
| Postfix | `.member`, `[index]`, `(args)` |
| Unary | `-x`, `not x` / `¬x` |
| Multiplicative | `*`, `/`, `×` (Cartesian product) |
| Additive | `+`, `-` |
| Set | `∩` (intersection) |
| Relational | `==`, `!=`, `<`, `<=`, `>`, `>=`, `∈`, `⊆` |
| Logical NOT | `not`, `¬` |
| Logical AND | `and`, `∧` |
| Logical OR | `or`, `∨` |

Both ASCII and Unicode forms of the logical and set operators are
accepted. Pick one and stay consistent.

### 6.4 Member access and indexing

```ccgnf
player.Hand            // characteristic or zone lookup on an entity
Arena[Left]            // index into a named template → an entity
p.aether_cap_schedule[round - 1]   // index into a list
```

Indexing accepts one or more index expressions separated by commas.
Use it for card fields too: `self.abilities[0]`.

### 6.5 Function calls

```ccgnf
DealDamage(target, 3)                    // positional
Draw(player, amount: 5)                  // named arg
Triggered(on: Event.X, effect: E)        // all-named style
Event.Custom(phase=Rise, player=p)       // pattern bindings (= instead of :)
```

Three argument shapes:

| Form | When | Example |
|------|------|---------|
| Positional | Regular calls. | `DealDamage(target, 3)` |
| Named (`name: value`) | Constructor-style calls. | `Draw(player, amount: 5)` |
| Binding (`name=value`) | Event patterns in `on:` clauses. The lowercase-first-letter of `value` indicates a fresh binding; see §8.3. | `on: Event.TurnBegin(player=p)` |

You may mix positional and named args; bindings are only meaningful
inside pattern-matching contexts (ability `on:` clauses).

### 6.6 Lambdas

```ccgnf
x -> x.cost <= 3
(x, y) -> x.power + y.power
```

Single-parameter lambdas omit the parens. Multi-parameter lambdas wrap
the list. Lambdas are values; pass them to `Target`, `ForEach`, etc.

---

## 7. Control flow

CCGNF has six control-flow constructs. All are **grammar forms**, not
ordinary function calls — they can choose which branches to evaluate.

### 7.1 `If(condition, then, else)`

```ccgnf
If(Count(p.Hand) == 0,
   EmitEvent(Event.Lose(player: p, reason: "empty_hand")),
   Draw(p, 1))
```

Strict three-arg. The `else` branch is required; use `NoOp` if you
don't need one.

### 7.2 `Switch(scrutinee, { Label: value, … })`

```ccgnf
Switch(phase, {
  Draw:   DrawPhase,
  Main:   MainPhase,
  Combat: CombatPhase,
  Default: NoOp
})
```

Labels are identifiers. Use `Default` for the fall-through.

### 7.3 `Cond([(pred, effect), …])`

```ccgnf
Cond([
  (self.life >= 20, Heal(self, 0)),
  (self.life >= 10, Heal(self, 2)),
  (Default,         Heal(self, 5))
])
```

First matching predicate wins.

### 7.4 `When(predicate, effect, options)`

Fires `effect` once when `predicate` becomes true. Options like
`check_at` and `apply_at` govern when the check and effect occur.

### 7.5 `let var = value in body`

```ccgnf
let weakest = MinBy(p.creatures, c -> c.toughness) in
  DealDamage(weakest, 2)
```

Lexical binding. Usable inside any expression.

### 7.6 `Sequence([e1, e2, e3, …])`

A builtin call, not a grammar form, but ubiquitous enough to list
here. Evaluates each element in order, left to right. Effects inside
mutate the state; the list itself returns nothing.

---

## 8. Abilities

Abilities are the unit of game logic. Every rule lives on an entity
as an ability. Five kinds:

### 8.1 Kinds

| Kind | Fires when | Typical use |
|------|-----------|-------------|
| `Static` | Continuously while the entity is in a valid zone. | State-based actions, characteristic overlays. |
| `Triggered` | On a matching event. | "When X happens, do Y." |
| `OnResolve` | When a card resolves (spells, abilities). | Card text that runs once when played. |
| `Replacement` | Before a specific event commits. May alter or cancel it. | "If X would happen, Y happens instead." |
| `Activated` | When a player pays a cost and uses it. | Tap-abilities, pay-to-use. |

### 8.2 Triggered ability shape

```ccgnf
Triggered(
  on:       Event.TurnBegin(player=p),    // pattern
  check_at: event_emission,                // default
  apply_at: after_event,                   // default
  effect:   Sequence([
    Draw(p, 1),
    SetCounter(p, mana, p.mana_cap[turn])
  ]))
```

**Required:** `on:`, `effect:`.
**Optional:** `check_at:`, `apply_at:` — govern timing windows; see
[`grammar/GrammarSpec.md`](../grammar/GrammarSpec.md) §8.

### 8.3 Event patterns

The `on:` value is matched against incoming events. Two shapes:

- **Bare:** `Event.TypeName` matches any event of that type.
- **Structured:** `Event.TypeName(field=value, other=name)` matches the
  type *and* each listed field.

Each structured field is either:

- **A literal** (match requirement). Identifiers starting with a
  capital letter, numeric literals, strings, and explicit expressions.
- **A binding** (captures the field value into the effect's scope).
  Identifiers starting with a **lowercase letter**.

```ccgnf
// Matches any TurnBegin, binds the player.
on: Event.TurnBegin(player=p)

// Matches only TurnBegin events for the Main phase.
on: Event.TurnBegin(player=p, phase=Main)

// Matches Destroy events for creatures only, binds the victim.
on: Event.Destroy(target=t, reason=lethal)
```

### 8.4 Static ability shape

```ccgnf
Static(
  modifies: BoardState,
  check_at: continuously,
  rule: ForEach(c -> c.kind == Creature ∧ c.toughness <= 0,
          EmitEvent(Event.Destroy(target: c, reason: lethal))))
```

`modifies:` names the sub-state the ability reads/writes (for tooling).
`check_at:` is usually `continuously` (every SBA pass).

### 8.5 Activated ability shape

```ccgnf
Activated(
  cost:     PayMana(self.controller, 2),
  once_per_turn: true,
  effect:   DealDamage(Target(t -> t.kind == Creature), 1))
```

Add `once_per_turn: true` for at-most-once activation. The engine
tracks a per-ability `used_this_turn` counter automatically when the
flag is set.

---

## 9. Runtime scope and naming

When an expression is evaluated, identifiers resolve in this order:

1. **Lexical scope:** lambda parameters, `let` bindings, `ForEach`
   iteration variables.
2. **Keyword literals:** `true`, `false`, `None`, `Unbound`, `NoOp`.
3. **Named entities:** concatenated base + index. `Player1` resolves
   to the entity generated by `Entity Player[i] for i ∈ {1,2}` with
   `i=1`.
4. **Symbols (fallback):** any unresolved identifier becomes a bare
   symbol (e.g., `Rise`). The Validator catches typos; at runtime,
   unknown symbols compare equal only to themselves.

`Arena[Left]` is syntactic sugar for the named entity `ArenaLeft`
(base + index concat).

---

## 10. Sentinels

Four special values are reserved. Use them deliberately.

| Sentinel | Meaning |
|----------|---------|
| `None` | "Set, but no value." A player with no opponent in a 1-player tutorial. |
| `Unbound` | "Not yet set." Game.active_player before first turn. |
| `NoOp` | A zero-cost, zero-effect ability body. Use in `If` else-branches or `Choice` arms. |
| `Default` | Fall-through label for `Switch` / `Cond`. |

`None` and `Unbound` compare truthy-false (`If(x, …, default)` where
`x` is `None` or `Unbound` goes to the default branch). `NoOp` is
truthy.

---

## 11. Events

Events are how things happen. Effects enqueue them; abilities listen
for them.

### 11.1 Constructing an event

```ccgnf
Event.TurnBegin(player: p, phase: Main)
```

Emit with `EmitEvent(...)`:

```ccgnf
EmitEvent(Event.Damage(target: t, amount: 3))
```

Fields can be named (`:`) or binding (`=`) — both evaluate at emit time.
The engine normalizes them into the event's payload.

### 11.2 Event type names

Pick names that read naturally on both the emit and the match sides.
Conventions used by the reference encoding (your game can use its
own):

| Type | When it fires |
|------|---------------|
| `GameStart` | Once, at the outer kick-off. |
| `PhaseBegin(phase, player)` | Per phase transition. |
| `Draw`, `Discard`, `Mill` | Hand/deck mutations. |
| `Damage(target, amount, source)` | Damage dealing. |
| `Destroy(target, reason)` | Destruction / zone-change-to-cache. |
| `Play(card, player)` | Card cast. |
| `Resolve(card, player)` | Card resolves. |
| `Win(player, reason)` / `Lose(player, reason)` | Terminal events. |

The engine is agnostic to which names you pick; match your
encoding's internal vocabulary.

### 11.3 Ordering

Events run FIFO from the pending queue. Within a single event's
dispatch, Triggered abilities on `Game` fire in **declaration order**.
If you need specific ordering, declare the triggers in the order you
want them to fire, or guard them with predicates.

---

## 12. Builtins

The v1 interpreter ships the following builtins. Full signatures +
semantics in [`docs/plan/reference/builtins.md`](plan/reference/builtins.md).

### Control flow

`Sequence([…])`, `ForEach(binding, body)`, `Repeat(n, body)`,
`Choice(chooser:, options: {…})`, `NoOp`, `Guard(…)`.

`ForEach` supports two binding shapes:

- Membership: `ForEach(x ∈ S, body)` — iterate over set/list `S`.
- Tuple: `ForEach((x, y) ∈ S₁ × S₂, body)` — iterate over Cartesian product.
- Lambda predicate (specified but not implemented in v1):
  `ForEach(x -> pred(x), body)` — iterate over every entity in state
  matching `pred`.

### Collections

`Count(zoneOrCollection)`, `Max(a, b, …)`, `Min(a, b, …)`.

### Event ops

`EmitEvent(Event.X(...))`.

### State mutation

| Builtin | Form |
|---------|------|
| `SetCounter(entity, name, value)` | `name` is an identifier, not evaluated. |
| `IncCounter(entity, name, delta)` | Treats missing as 0. |
| `ClearCounter(entity, name)` | Sets to 0. |
| `SetCharacteristic(entity, name, value)` | Arbitrary value. |
| `SetFlag(path, bool)` | Set a boolean state flag. |

### Deck operations

| Builtin | Form |
|---------|------|
| `Shuffle(zone)` | Fisher–Yates, seeded. |
| `Draw(player)` / `Draw(player, n)` / `Draw(player, amount: n)` | Moves up to `n` from end of Deck to Hand. |

### Randomness and instantiation

| Builtin | Form |
|---------|------|
| `RandomChoose(value: S, bind: path)` | Picks one; writes to `path` if it's a member access on an entity. |
| `InstantiateEntity(kind: K, owner: o, initial_counters: {…}, …)` | Allocates a fresh entity. |

### Game-specific builtins (reference encoding)

`RefillAether`, `PayAether`, `other_player`, `TurnOrderFrom` ship in
v1 because they're used by the reference game. If you encode a new
game with different resources:

- **Option A** (preferred, no engine changes): define macros that wrap
  `SetCounter` / `IncCounter`:

  ```ccgnf
  define RefillMana(p, amount) = SetCounter(p, mana, amount)
  define PayMana(p, cost) = IncCounter(p, mana, -cost)
  define OtherPlayer(p) = If(p == Player1, Player2, Player1)
  ```

- **Option B**: add a new builtin to `src/Ccgnf/Interpreter/Builtins.cs`.
  Only do this for engine-core operations, not game-specific helpers.

### Inert stubs

Some names parse but are no-ops in v1 (`OpenTimingWindow`,
`DrainTriggersFor`, `BeginPhase`, `Target`, `MoveTo`,
`abilities_of_permanents`). These land in later engine passes.

---

## 13. Rulings

When the grammar or spec leaves an ambiguity, resolve it in the
encoding via a ruling:

```ccgnf
// encoding/engine/00-rulings.ccgnf
// Ruling R-1: a creature with 0 toughness is destroyed immediately,
//              after all Replacement abilities have had a chance to
//              intercept.
// Ruling R-2: ...
```

Rulings are comments but the Validator cross-references them by
identifier. Use them sparingly — they should resolve real ambiguities,
not document clean design.

---

## 14. Forbidden constructs

CCGNF rejects these at parse time:

- `Procedure NAME(args): …` blocks. Game logic is abilities on
  entities, not top-level procedures. If you're writing imperative
  game logic, the rule is under-decomposed.
- Top-level imperative statements. Only declarations at file top level.
- Numeric step labels (`Step 1 — …`) outside comments.

The Validator re-checks these after macro expansion.

---

## 15. Worked example — a minimal game

A complete, compiling encoding of a tiny two-player card duel: each
player starts with 20 life, draws 7 cards, and plays once per turn
until someone reaches 0.

```ccgnf
// example/common/00-combinators.ccgnf
define OtherPlayer(p) = If(p == Player1, Player2, Player1)
```

```ccgnf
// example/engine/01-entities.ccgnf
Entity Game {
  kind: Game
  characteristics: { turn: 1, active_player: Unbound }
  abilities: []
}

Entity Player[i] for i ∈ {1, 2} {
  kind: Player
  characteristics: { starting_life: 20, hand_size: 7 }
  counters:        { life: 20 }
  zones: {
    Deck:       Zone(order: sequential, visibility: face_down_private),
    Hand:       Zone(capacity: 10, order: unordered, visibility: private_to_owner),
    Battlefield: Zone(order: unordered, visibility: public),
    Graveyard:  Zone(order: FIFO, visibility: public)
  }
  abilities: []
}
```

```ccgnf
// example/engine/02-setup.ccgnf
Game.abilities += Triggered(
  on:     Event.GameStart,
  effect: Sequence([
    RandomChoose(value: {Player1, Player2}, bind: Game.active_player),
    ForEach(p ∈ {Player1, Player2}, Shuffle(p.Deck)),
    ForEach(p ∈ {Player1, Player2}, Draw(p, amount: 7)),
    EmitEvent(Event.TurnBegin(player: Game.active_player))
  ]))

Game.abilities += Triggered(
  on:     Event.TurnBegin(player=p),
  effect: Sequence([
    Draw(p, 1),
    OpenTimingWindow(main_phase, owner=p)
  ]))
```

```ccgnf
// example/engine/03-sba.ccgnf
// A Player at <= 0 life loses.
Game.abilities += Static(
  modifies: BoardState,
  check_at: continuously,
  rule: ForEach(p -> p.kind == Player ∧ p.life <= 0,
          EmitEvent(Event.Lose(player: p, reason: "life_depleted"))))
```

```ccgnf
// example/cards/red.ccgnf
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

That's a compile-and-validate-clean CCGNF program. Extend it with more
cards, more phases, and game-specific keywords; the primitives stay the
same.

---

## 16. Validating your encoding

From the repo root:

```bash
make ci                                       # Linux / WSL
make DOTNET=dotnet.exe ci                     # Windows host
```

Or use the REST host:

```bash
make rest
curl -X POST http://localhost:19397/api/validate \
  -H 'content-type: application/json' \
  -d '{"files": [{"path": "example/engine/01-entities.ccgnf",
                   "content": "Entity Foo { kind: Foo }"}]}'
```

The `/api/validate` endpoint returns `{ "ok": true/false, diagnostics: […] }`
with structured errors. See [docs/plan/api/rest.md](plan/api/rest.md) for
the full REST surface.

---

## 17. Where to look for more

- **Ability dispatch + event loop detail:** [`grammar/GrammarSpec.md`](../grammar/GrammarSpec.md) §8.
- **Validator rules:** [`docs/plan/reference/ccgnf-lib.md`](plan/reference/ccgnf-lib.md) — Validator section.
- **The builtin catalog:** [`docs/plan/reference/builtins.md`](plan/reference/builtins.md).
- **AST shape for tooling:** [`docs/plan/reference/ast-nodes.md`](plan/reference/ast-nodes.md).
- **Reference encoding:** [`encoding/`](../encoding/).

If you're encoding a brand new game, the companion skill walks an agent
through it step by step. See [`.claude/skills/encode-ccg/SKILL.md`](../.claude/skills/encode-ccg/SKILL.md).
