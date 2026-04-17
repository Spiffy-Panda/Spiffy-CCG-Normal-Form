# RESONANCE — CCG-NF Encoding

*A full normal-form encoding of the Resonance CCG, companion to `GameRules.md` and `Supplement.md`. Everything in the game (players, phases, zones, state-based actions, keywords, cards) is expressed in the same six primitives and a small macro library on top.*

---

## 0. Conventions

**Syntax.** YAML-ish for entity declarations (name-value pairs and nested blocks). S-expression-ish for abilities and effects (`Kind(arg: value, ...)`, nested). Macros declared with `define NAME(params) = body` and applied by textual substitution.

**Identifiers.** `self` inside an ability body is always the Entity bearing the ability. `event` is the triggering Event. `target` is the bound Target of the ability's Target binding. `self.controller`, `self.owner`, `self.arena` are Entity attributes.

**Comments.** `//` line comments are non-semantic.

**Natural text.** For every card I include its original English rules text as a `// text:` comment so the encoding is reviewable against the source.

**Rulings.** Ambiguities in GameRules that this encoding resolves. Treat as canon until GameRules is updated; any change in GameRules must propagate here.

- **R-1 — Recur vs Conduit Collapse.** When a Conduit is destroyed, its controller's Units in that Arena *are destroyed* (not merely moved). The destruction emits `Event.Destroy(target=u, reason: conduit_collapse)` for each such Unit. Recur, being a Replacement on destruction, fires and redirects the Unit to the bottom of the Arsenal. This matches GameRules §7.2 which lists Conduit-collapse as a *removal* path equivalent to destruction.
- **R-2 — Countered Maneuver Push.** A countered Maneuver still Pushes its Echo. "Playing a card" is established at announcement and committed at cost-paid; Push is a consequence of that commitment, not of successful resolution. Step 6 of the play protocol fires regardless of whether Step 5 was replaced by a counter. This is also required for The Last Whisper's `push_faction_override` to do anything meaningful.
- **R-3 — Sprawl from a Maneuver.** GameRules §11 says Sprawl creates Saplings "in this Arena," which is only well-defined for Units and Arena-attached Standards. When Sprawl fires from a Maneuver (no intrinsic Arena), the Maneuver's controller targets a non-Collapsed Arena at resolution time; Saplings enter there.
- **R-4 — Damage heal timing.** GameRules §5 Rise says "clear all damage from your Units" and §7.3 says "Ramparts heal at end of Fall." These are contradictory windows. This encoding follows §7.3 (end of Fall) as canonical because it is the more specific rule; §5 Rise's clear-damage line becomes a no-op in practice. A single heal point means damage from an opponent's turn persists through your own next turn of play, which matches design intent.
- **R-5 — Interrupt Debt uses *printed* cost.** Debt is `2 × printed_cost`, never `2 × effective_cost`. Future Interrupts with cost modifiers (Surge, Phantom discounts) pay their modifier benefit in the current play but do not reduce the Debt burden.
- **R-6 — Interrupt Debt cap against *next* Rise.** The cap is "your next Rise's Aether refresh amount" — specifically `next_rise_refresh(p)` (defined below), not the current round's cap. When you are the first player and take Debt on the second player's turn, your next Rise is next round and its cap is higher than this round's.

---

## 1. Schema Reference

Six primitives:

- **Entity** — identified bundle of Characteristics, DerivedCharacteristics, Counters, Tags, a Zone, owner/controller, and Abilities. Kinds: Card, Player, Zone, Phase, Game, Arena, Conduit, Echo, Token, Standard, ContinuousModifier, PlayBinding (ephemeral; see §4.5.1).
- **Zone** — structured collection of Entities. Parameters: `capacity`, `order` (unordered | LIFO | FIFO | sequential), `on_overflow` (reject | EvictLeftmost | EvictOldest), `visibility` (public | private_to_owner | face_down), `adjacency` (optional relation to other zones).
- **Event** — typed record: `{type, source, target, payload, phase, timestamp}`.
- **Effect** — algebraic term over atomic ops (`DealDamage`, `Heal`, `Draw`, `Mill`, `MoveTo`, `PayAether`, `SetState`, `ClearState`, `IncCounter`, `CreateToken`, `EmitEvent`, `NoOp`, …) and combinators (`Sequence`, `Choice`, `ForEach`, `Target`, `If`, `When`, `ChooseHighest`, `ScheduleAt`, `Replace`, `Prevent`).
- **Ability** — (kind, trigger, guard, cost, effect) quadruple attached to an Entity. Kinds: Static, Triggered, OnResolve, Replacement, Activated.
- **Predicate / Selector** — expressions over game state and events. Lambda syntax: `x -> x.cost <= 3`.

Characteristic layering (three layers, applied in order):
1. **Base / Copy** — initial characteristic values; `Copy(other)` effects.
2. **Stat modifiers** — `+N/+N`, Fortify, ongoing +Force, Ramparts reductions.
3. **Ability grants/removes** — "gains Sentinel", "loses all abilities".

---

## 2. Ability Lifecycle — Explicit Timing

Every ability has up to five checkpoints: **register**, **check**, **cost** (Activated only), **apply**, **deregister**. The kind of ability determines the defaults for *when* check and apply occur. Default timings per kind:

| Kind | Register | Check (when condition evaluated) | Apply (when effect fires) | Deregister |
|---|---|---|---|---|
| **Static** | on entering active zone | continuously, on-demand during any state query | continuously, by rewriting the query's answer | on leaving active zone |
| **Triggered** | on entering active zone | `event_emission` — the instant a matching Event is emitted | `after_event` — after the triggering action completes, queued into the current timing window | on leaving, or on apply if one-shot |
| **OnResolve** | on announcement (step 1 of play protocol) | `card_resolving` — step 5 of play protocol | `card_resolving` — same step, after check | after one apply |
| **Replacement** | on entering active zone | `event_pending` — before the event commits to the event stream | `event_commit` — in place of the triggering event; original does not occur as specified | on leaving, or when per-lifetime budget exhausted |
| **Activated** | on entering active zone | `activation_declared` — when controller declares and costs are legal | `after_cost_paid` — immediately after cost payment resolves | on leaving |

**Explicit overrides.** Any ability may override its default `check_at` and `apply_at` by naming an alternate window. Windows defined in GameRules §5.1 are first-class:

```
windows := { announcement, cost_computation, cost_payment, tier_binding,
             post_tier_interrupt, card_resolving, resolution,
             start_of_turn, end_of_turn, start_of_clash, end_of_clash,
             event_emission, event_pending, event_commit, after_event,
             activation_declared, after_cost_paid,
             continuously, immediate }
```

**Triggered-vs-Replacement test.** If the triggering event still occurs in full after the ability fires → **Triggered**. If the event is replaced or prevented → **Replacement**.

**Guards re-checked at apply?** Default: no. Guard is checked at `check_at` only. To re-check at apply (the MTG "intervening-if" pattern), annotate `recheck: true`.

**Resonance tier binding is explicit.** Tiers evaluate at `tier_binding` (play protocol step 4, before Push). The result is stored in the `PlayBinding.tier_snapshot`. The effect at `card_resolving` (step 5) reads that snapshot, *not* the live ResonanceField. This matters when Interrupts fire between steps 4 and 5 and alter the Field — the snapshot is frozen.

**Once-per-turn annotations.** An ability may be annotated:

- `once_per_turn: true` — the ability cannot fire a second time this turn; the harness auto-increments `used_this_turn` on fire, and suppresses further fires while it is ≥ 1. The Rise phase ability (§4.3) clears this counter on each of the controller's turns.
- `once_per_turn_per_trigger_subject: <expr>` — same gate, but keyed by the value of `<expr>` in the triggering event (e.g., `event.card` for Eremis). A subject that has already been seen this turn cannot trigger the ability again; new subjects trigger normally.

No other gating modes exist in the current card set. A new mode is a harness-level change, not a card-level one.

---

## 3. Macro Library

### 3.1 Resonance, Tiers, Banner

```
// Count of Echoes of faction F in controller's current ResonanceField.
define CountEcho(F) =
  Count(self.controller.ResonanceField, e -> F ∈ e.factions)

// Tier predicate, to be evaluated at tier_binding.
define Resonance(F, N) = (CountEcho(F) >= N)

define Peak(F) = Resonance(F, 5)

define Banner(F) = (self.controller.banner == F)

define BannerExists() = (self.controller.banner != NONE)

// Tier table: highest-satisfied case wins, others skip.
// Cases are evaluated in order; first satisfied predicate's effect fires.
// Evaluated at tier_binding; effect applied at card_resolving.
define Tiers(cases...) =
  ChooseHighest(
    cases,
    check_at: tier_binding,
    apply_at: card_resolving)

// Independent gated lines: each independently fires if its predicate is met.
define Lines(lines...) =
  Sequence([
    When(pred, eff, check_at: tier_binding, apply_at: card_resolving)
    for (pred, eff) in lines
  ])
```

### 3.2 Trigger shorthands

```
define OnEnter(effect) =
  Triggered(
    on:     Event.EnterPlay(target=self),
    effect: effect)

define OnPlayed(effect) =
  Triggered(
    on:       Event.CardPlayed(card=self),
    check_at: announcement,
    apply_at: before_resolution,
    effect:   effect)

define StartOfYourTurn(effect) =
  Triggered(
    on:       Event.PhaseBegin(phase=Rise, player=self.controller),
    apply_at: after_rise_draw,   // per §5.1 "start of your turn" window
    effect:   effect)

define EndOfYourTurn(effect) =
  Triggered(
    on:     Event.PhaseBegin(phase=Fall, player=self.controller),
    effect: effect)

define StartOfClash(effect) =
  Triggered(
    on:     Event.PhaseBegin(phase=Clash),
    effect: effect)

define EndOfClash(effect) =
  Triggered(
    on:     Event.PhaseEnd(phase=Clash),
    effect: effect)

// "When another X enters my Arena..."
define OnArenaEnter(filter, effect) =
  Triggered(
    on:     Event.EnterPlay(target -> target != self
                          ∧ target.arena == self.arena
                          ∧ filter(target)),
    effect: effect)

// "When you play a card of type T/faction F..."
define OnCardPlayed(filter, effect) =
  Triggered(
    on:       Event.CardPlayed(c -> c.controller == self.controller ∧ filter(c)),
    check_at: announcement,
    effect:   effect)
```

### 3.3 Cost, target, and card-level conveniences

```
// Persistent card-attached cost modifier (e.g., Phantom's cumulative -1).
define ReduceBaseCost(n, floor) =
  IncCounter(self, base_cost_reduction, n, persistent: true, floor: floor)

// "Destroy" per §7.3: damage-reaching-zero or explicit destroy maps to MoveTo(Cache).
define Destroy(target) =
  MoveTo(target, target.owner.Cache)

// Damage to Units reduces current_ramparts counter; to Conduits reduces integrity.
// Excess is lost.
define DealDamage(target, amount) =
  Atomic.DealDamage(target, amount)   // primitive; zone-aware

// Push-an-echo is a Game-level protocol step, not a card ability. Cards that
// "push an extra Echo" use this helper:
define PushEcho(factions) =
  MoveTo(
    NewEcho(factions: factions),
    self.controller.ResonanceField)   // FIFO-5, evicts leftmost
```

### 3.4 Keyword Macros

**EMBER**

```
// Surge: this card costs 1 less if another EMBER Echo was Pushed this turn.
// Check at cost_computation (step 2 of play protocol).
define Keyword.Surge() =
  Static(
    modifies: Characteristic(self, effective_cost),
    check_at: cost_computation,
    rule: if (Count(self.controller.pushes_this_turn,
                    e -> e.card != self ∧ EMBER ∈ e.factions) >= 1)
          then subtract 1 (floor: 0)
          else no change)

// Blitz: ignores Deployment Sickness on the turn it is deployed.
define Keyword.Blitz() =
  Static(
    modifies: DeploymentSickness(self),
    rule: never_applies)

// Ignite X: start-of-turn damage to opposing Units with low Ramparts in this Arena.
define Keyword.Ignite(X) =
  StartOfYourTurn(
    ForEach(
      u -> u.arena == self.arena
         ∧ u.controller != self.controller
         ∧ u.current_ramparts <= 2,
      DealDamage(u, X)))
```

**BULWARK**

```
// Fortify X: +X Ramparts while controller's Conduit in this Arena has >= 4 Integrity.
// Note: suppresses (not destroys) when Conduit drops below 4; returns if healed back.
define Keyword.Fortify(X) =
  Static(
    modifies: Characteristic(self, ramparts, layer: 2),
    rule: if (self.controller.Conduit(self.arena).integrity >= 4)
          then add X
          else no change)

// Mend X: on entering play, heal the Conduit in this Arena by X (capped at starting).
// Fizzles if Arena is Collapsed for controller.
define Keyword.Mend(X) =
  OnEnter(
    If(self.controller.Conduit(self.arena) exists ∧ not collapsed_for(self.controller, self.arena),
       Heal(self.controller.Conduit(self.arena), X, cap: starting_integrity),
       NoOp))

// Sentinel: this Unit contributes 0 Force to Projected Force;
//           its Force is instead added to controller's Fortification in this Arena.
define Keyword.Sentinel() =
  Static(
    modifies: ClashContribution(self),
    rule: { projected_force: 0, fortification_bonus: self.force })
```

**TIDE**

```
// Drift: optional move to an adjacent non-collapsed Arena at end of your turn.
define Keyword.Drift() =
  EndOfYourTurn(
    Choice(self.controller, {
      NoOp,
      Target(a -> a ∈ adjacent(self.arena)
                ∧ not collapsed_for(self.controller, a),
             MoveUnitToArena(self, target))
    }))

// Recur: replaces destroy-to-Cache with destroy-to-bottom-of-Arsenal.
// Does not fire if destruction is prevented or redirected to exile.
define Keyword.Recur() =
  Replacement(
    on:           Event.MoveTo(target=self, destination=self.owner.Cache),
    replace_with: MoveTo(self, bottom_of(self.owner.Arsenal)))

// Reshape: move one Echo one slot on own ResonanceField. Appears on Maneuvers
// as an at-announcement option, check_at: announcement.
define Reshape(n) =
  Repeat(n,
    Choice(self.controller, {
      NoOp,
      Target(e -> e ∈ self.controller.ResonanceField,
        Target(dir -> dir ∈ {left, right},
          SwapEchoPosition(target_e, direction: dir, distance: 1)))
    }),
    check_at: announcement,
    apply_at: announcement)
```

**THORN**

```
// Rally: triggers when another friendly Unit enters this Unit's Arena.
define Keyword.Rally() =
  OnArenaEnter(
    filter: u -> u.type == Unit ∧ u.controller == self.controller,
    effect: ModifyCharacteristic(self, force, +1, duration: end_of_turn, layer: 2))

// Sprawl X: create X 1/1 Sapling tokens in self.arena. Tokens do not Push.
define Keyword.Sprawl(X) =
  ForEach(i -> i in [1..X],
    CreateToken(
      template: ThornSapling,
      zone:     self.arena,
      owner:    self.controller,
      controller: self.controller))

// Kindle (on Standards only): counter accumulation with fire-and-reset at 3.
// The specific on-fire effect is supplied per-card via `Kindle: <effect>`.
define Keyword.Kindle(effect_on_fire) =
  Sequence([
    StartOfYourTurn(IncCounter(self, kindle, 1)),
    Static(
      modifies: nothing,
      check_at: counter_change(self, kindle),
      trigger_effect_if: self.counters.kindle >= 3,
      then: Sequence([
        effect_on_fire,
        ResetCounter(self, kindle, 0)
      ]))
  ])
```

**HOLLOW**

```
// Phantom: at Start of Clash, controller may declare. If declared, Unit contributes
// 0 Force AND 0 Fortification this Clash, returns to hand at End of Clash, and its
// base cost is permanently reduced by 1 (min 0, persistent across zones).
define Keyword.Phantom() =
  StartOfClash(
    Choice(self.controller, {
      NoOp,
      Sequence([
        SetState(self, Phantoming, duration: until_end_of_clash),
        ScheduleAt(end_of_clash,
          Sequence([
            MoveTo(self, self.owner.Hand),
            ReduceBaseCost(1, floor: 0)
          ]))
      ])
    }))

// Note: Clash-resolution procedure (see §4.6) reads the Phantoming state and
// overrides both the projected_force AND fortification contributions to 0.

// Shroud: cannot be the chosen target of opposing effects; sweepers and
// self-targeted effects are unaffected.
define Keyword.Shroud() =
  Static(
    modifies: TargetLegality(self),
    rule: if (targeting_ability.controller != self.controller
           ∧ targeting_ability.selection_mode == explicit_target)
          then illegal
          else legal)

// Pilfer X: opponent reveals hand; you choose up to X cards to exile
// to the bottom of their Arsenal.
define Pilfer(X) =
  Sequence([
    RevealHand(opponent_of(self.controller)),
    Target(
      selector: cards -> cards ⊆ opponent_of(self.controller).Hand
                       ∧ |cards| <= X,
      chooser:  self.controller,
      effect:   ForEach(c -> c ∈ target_cards,
                  MoveTo(c, bottom_of(opponent_of(self.controller).Arsenal),
                         visibility: exiled)))
  ])
```

**Universal**

```
// Deployment Sickness: a state set when a Unit enters an Arena via play.
// Cleared at the start of controller's next Rise phase. Suppressed by Blitz.
// Read by the Clash-resolution procedure to force projected_force to 0.
define Keyword.DeploymentSickness() =
  Sequence([
    Triggered(
      on:     Event.EnterPlay(target=self),
      guard:  not has_keyword(self, Blitz),
      effect: SetState(self, DeploymentSickness)),
    Triggered(
      on:     Event.PhaseBegin(phase=Rise, player=self.controller),
      effect: ClearState(self, DeploymentSickness))
  ])

// Interrupt: marks a Maneuver playable on either player's turn during Channel,
// in response to a declared card or trigger, before it resolves.
// Debt accrual handled by the play protocol (§4.5).
define Keyword.Interrupt() =
  Static(
    modifies: PlayWindows(self),
    rule: { opponent_turn_channel: allowed,
            response_to_pending: allowed })

// Unique: controller may have only one Entity with this name-key.
// Duplicate entering play is sent to Cache immediately.
define Keyword.Unique() =
  Replacement(
    on:           Event.EnterPlay(target=self),
    guard:        exists other: other.name == self.name
                             ∧ other.controller == self.controller
                             ∧ other != self
                             ∧ other.zone is battlefield,
    replace_with: MoveTo(self, self.owner.Cache))
```

---

## 4. The Game

### 4.1 Global Entities

```
Entity Game {
  kind: Game
  characteristics: { round: 1, first_player: <determined at setup> }
  child_zones: { Arena[Left], Arena[Center], Arena[Right] }
  abilities: [ ... ]   // see §4.3–§4.7
}

Entity Arena[pos] for pos ∈ {Left, Center, Right} {
  kind: Arena
  characteristics: { position: pos }
  // Adjacency: Left↔Center, Center↔Right. Left and Right are NOT adjacent.
  adjacency: case pos of
    Left   -> { Center }
    Center -> { Left, Right }
    Right  -> { Center }
  child_zones: {
    units[Player1], units[Player2],
    standards[Player1], standards[Player2],
    conduit[Player1], conduit[Player2]
  }
  state_flags: { collapsed_for[Player1]: false, collapsed_for[Player2]: false }
}

Entity Player[i] for i ∈ {1, 2} {
  kind: Player
  characteristics: {
    integrity_start: 7,
    aether_cap_schedule: [3,4,5,6,7,8,9,10,10,10,...],
    hand_cap: 10,
    max_mulligans: 2
  }
  counters: {
    aether: 0,
    debt: 0,
    pushes_this_turn: []   // list of {card, factions} per turn, cleared at Fall
  }
  derived: {
    banner: mode({e.factions | e ∈ self.ResonanceField ∧ e.factions ≠ {NEUTRAL}},
                 tiebreak: most_recent,
                 undefined_when: empty)
  }
  zones: {
    Arsenal:        Zone(order: sequential, visibility: face_down_private),
    Hand:           Zone(capacity: 10, order: unordered, visibility: private_to_owner,
                         on_overflow: discard_down_at_end_of_turn),
    Cache:          Zone(order: FIFO, visibility: public),
    ResonanceField: Zone(capacity: 5, order: FIFO, on_overflow: EvictLeftmost,
                         visibility: public),
    Void:           Zone(order: unordered, visibility: public)   // "exile"
  }
  abilities: []   // player-scoped rules can attach here
}

Entity Conduit[owner, arena] {
  kind: Conduit
  owner: owner
  arena: arena
  characteristics: { starting_integrity: 7 }
  counters: { integrity: 7 }
  zone: Arena[arena].conduit[owner]
  // Conduits are NOT Permanents. They are targeted by name: "target Conduit".
}
```

### 4.2 Setup

Setup is a single Triggered ability on the Game entity, fired by `Event.GameStart` (emitted by the host harness as game kick-off). Every step is a declarative Effect term, composed via `Sequence` and named macros. No procedural loops.

```
Game.abilities += Triggered(
  on:       Event.GameStart,
  check_at: event_emission,
  apply_at: after_event,
  effect:   SetupSequence())

define SetupSequence() = Sequence([
  ChooseFirstPlayer,
  InitializeConduits,
  InitializeDecks,
  InitialDraws,
  MulliganPhase,
  StartFirstRound
])

define ChooseFirstPlayer = Sequence([
  RandomChoose(value: {Player1, Player2}, bind: Game.first_player),
  EmitEvent(Event.FirstPlayerChosen(player: Game.first_player))
])

define InitializeConduits =
  ForEach((p, a) ∈ {Player1, Player2} × {Left, Center, Right},
    InstantiateEntity(
      kind:             Conduit,
      owner:            p,
      arena:            a,
      initial_counters: { integrity: 7 }))

define InitializeDecks =
  ForEach(p ∈ {Player1, Player2},
    Shuffle(p.Arsenal))

define InitialDraws = Sequence([
  Draw(Game.first_player,                amount: 5),
  Draw(other_player(Game.first_player),  amount: 6)
])

// Mulligan: two optional passes per player, in turn order from the first player.
// Each Mulligan returns >= 1 card, bottom-stacks them in chooser-determined order,
// and re-draws one fewer card than returned (minimum 0).
define MulliganPhase =
  ForEach(p ∈ TurnOrderFrom(Game.first_player),
    Repeat(Game.max_mulligans,
      Choice(chooser: p, options: {
        pass:     NoOp,
        mulligan: PerformMulligan(p)
      })))

define PerformMulligan(p) = Sequence([
  Target(
    selector: cards -> cards ⊆ p.Hand ∧ |cards| >= 1,
    chooser:  p,
    bind:     returned_cards),
  Target(
    selector: ordering -> IsPermutationOf(ordering, returned_cards),
    chooser:  p,
    bind:     bottom_order,
    effect:   ForEach(c ∈ bottom_order in index_order,
                MoveTo(c, bottom_of(p.Arsenal)))),
  Draw(p, amount: Max(0, |returned_cards| - 1))
])

define StartFirstRound = Sequence([
  SetCounter(Game, round, 1),
  EmitEvent(Event.PhaseBegin(phase: Rise, player: Game.first_player))
])
```

### 4.3 Turn Structure (Abilities on the Game)

A turn is a sequence of five Phase entities. Transitions are Events emitted by the Game entity. Rules are Triggered abilities on Game.

```
// Rise phase.
// Note per R-4: Ramparts healing lives at end of Fall (§4.3 Fall ability), not here.
Game.abilities += Triggered(
  on:     Event.PhaseBegin(phase=Rise, player=p),
  effect: Sequence([
    // Aether refresh, adjusted by Debt.
    RefillAether(p,
      amount: Max(0, p.aether_cap_schedule[Game.round - 1] - p.debt)),
    SetCounter(p, debt, 0),

    // Reset once-per-turn activation flags on p's permanents' abilities.
    ForEach(a -> a ∈ abilities_of_permanents(p) ∧ a.once_per_turn == true,
      ClearCounter(a, used_this_turn)),

    // Draw; empty Arsenal => lose.
    If(|p.Arsenal| == 0,
       EmitEvent(Event.Lose(player=p, reason: "deck_out")),
       Draw(p, 1)),

    // "Start of your turn" window opens; all start-of-turn triggers queue here.
    OpenTimingWindow(start_of_turn, owner=p),
    DrainTriggersFor(window=start_of_turn)
  ]))

// Channel phase
Game.abilities += Triggered(
  on: Event.PhaseBegin(phase=Channel, player=p),
  effect: EnterMainPhase(p))   // see §4.5 for play protocol

// Clash phase
Game.abilities += Triggered(
  on: Event.PhaseBegin(phase=Clash, player=p),
  effect: ResolveClash(active_player: p))   // see §4.6

// Fall phase. End-of-Fall is where Ramparts heal (per GameRules §7.3 / R-4).
Game.abilities += Triggered(
  on: Event.PhaseBegin(phase=Fall, player=p),
  effect: Sequence([
    OpenTimingWindow(end_of_turn, owner=p),
    DrainTriggersFor(window=end_of_turn),
    DiscardDownTo(p, cap: 10),   // excess cards go to Cache
    ClearCounter(p, pushes_this_turn),
    // Heal Ramparts on all of p's Units to their current maximum.
    ForEach(u -> u.controller == p ∧ u.type == Unit ∧ u.zone ∈ battlefield,
      SetCounter(u, current_ramparts, max_ramparts_of(u)))
  ]))

// Pass
Game.abilities += Triggered(
  on: Event.PhaseBegin(phase=Pass, player=p),
  effect: Sequence([
    If(other_player(p) == Game.first_player,
       IncCounter(Game, round, 1),
       NoOp),
    BeginPhase(Rise, player=other_player(p))
  ]))

// Note: there is NO turn-1 first-player draw-skip. GameRules §4 designates the
// second-player's draw-6 as "the sole compensation for going second"; no further
// asymmetry exists. The first player draws normally on their Round 1 Rise.
```

### 4.4 State-Based Actions

Checked after every resolved effect and at every window boundary.

```
// SBA: a Unit with current_ramparts <= 0 is destroyed (to Cache, unless Recur etc.).
Game.abilities += Static(
  modifies: BoardState,
  check_at: continuously,
  rule: ForEach(u -> u.type == Unit ∧ u.zone ∈ battlefield ∧ u.current_ramparts <= 0,
          EmitEvent(Event.MoveTo(target=u, destination=u.owner.Cache))))

// SBA: a Conduit with integrity <= 0 triggers Arena Collapse for its owner.
// Per R-1, Unit cleanup is a destruction event (so Recur can fire). Standards
// attached to the collapsing Arena are also destroyed, but Standards are
// non-Recur'able so we use the generic destroy path for both.
Game.abilities += Static(
  modifies: BoardState,
  check_at: continuously,
  rule: ForEach(c -> c.kind == Conduit
                   ∧ c.integrity <= 0
                   ∧ not collapsed_for(c.owner, c.arena),
          Sequence([
            SetFlag(Arena[c.arena].collapsed_for[c.owner], true),
            ForEach(u -> u.controller == c.owner
                       ∧ u.arena == c.arena
                       ∧ u.type == Unit,
              EmitEvent(Event.Destroy(target: u, reason: conduit_collapse))),
            ForEach(s -> s.controller == c.owner
                       ∧ s.type == Standard
                       ∧ s.subtype == attaches_to_arena
                       ∧ s.attached_arena == c.arena,
              EmitEvent(Event.Destroy(target: s, reason: conduit_collapse)))
          ])))

// SBA: if two of a player's Conduits are destroyed, opponent wins.
Game.abilities += Static(
  modifies: BoardState,
  check_at: continuously,
  rule: ForEach(p -> count({c : c.kind == Conduit ∧ c.owner == p ∧ c.integrity <= 0}) >= 2,
          EmitEvent(Event.Lose(player=p, reason: "two conduits destroyed"))))

// SBA: simultaneous Lose events => Draw.
Game.abilities += Static(
  modifies: BoardState,
  check_at: continuously,
  rule: If(pending_lose_events_for(Player1) ∧ pending_lose_events_for(Player2),
           EmitEvent(Event.GameEnd(result: Draw)),
           ForEach(p -> pending_lose_events_for(p),
             EmitEvent(Event.GameEnd(result: Win(other_player(p)))))))
```

### 4.5 Playing a Card — Event Chain

Playing a card is an Activated ability on the Player. It emits a chain of events; each event transition is a Game-level Triggered ability that does its step's work and emits the next event. There is no top-level procedure — the protocol *is* the sum of these abilities.

#### 4.5.1 The PlayBinding entity

The binding is a short-lived Entity that carries per-play state across the chain.

```
Entity PlayBinding {
  kind: PlayBinding
  lifetime: ephemeral         // destroyed on Event.PlayCard.Pushed
  characteristics: {
    player:                  Player,
    card:                    Card,
    targets:                 List<Target>,
    arena:                   Arena | None,
    play_source:             {from_hand, from_copy, from_replay},
    effective_cost:          int | Unbound,
    tier_snapshot:           TierSnapshot | Unbound,
    resolved:                bool = false,
    countered:               bool = false,
    push_faction_override:   Factions | None = None   // used by The Last Whisper
  }
}
```

#### 4.5.2 The player-facing ability

```
Player.abilities += Activated(
  name:  "PlayCard",
  args:  { c: Card, targets: List<Target>, arena: Arena | None },
  guard: And([
    c ∈ self.Hand,
    Or([
      // Normal play: own turn, Channel phase.
      Game.active_player == self ∧ Game.current_phase == Channel,
      // Reactive play: Interrupt, either player's Channel phase, inside an
      // open interrupt window (announcement or post_tier_interrupt).
      has_keyword(c, Interrupt)
        ∧ Game.current_phase == Channel
        ∧ in_any_interrupt_window(Game)
    ])
  ]),
  cost:  NoOp,     // Aether cost is paid at the CostPaid step of the chain.
  effect: Sequence([
    InstantiateEntity(
      kind:            PlayBinding,
      player:          self,
      card:            c,
      targets:         targets,
      arena:           arena,
      play_source:     from_hand,
      bind:            b),
    EmitEvent(Event.PlayCard.Announced(binding: b))
  ]))
```

A copy-effect that replays a Maneuver (Eremis) instantiates a PlayBinding with `play_source: from_copy` and skips straight to `CostPaid` (see §4.5.4). This is how copies bypass the cost and the Push while still running resolution.

#### 4.5.3 The event chain

```
// Step 1 -> Step 2: drain announcement-window triggers (OnPlayed, interrupts).
Game.abilities += Triggered(
  on:     Event.PlayCard.Announced(binding: b),
  effect: Sequence([
    OpenTimingWindow(announcement, binding: b),
    DrainTriggersFor(window: announcement),
    EmitEvent(Event.PlayCard.CostComputationBegan(binding: b))
  ]))

// Step 2: compute effective cost.
Game.abilities += Triggered(
  on:     Event.PlayCard.CostComputationBegan(binding: b),
  effect: Sequence([
    OpenTimingWindow(cost_computation, binding: b),
    SetCharacteristic(b, effective_cost,
      Max(0, b.card.printed_cost + sum_of_cost_modifiers(b.card, b.player))),
    EmitEvent(Event.PlayCard.CostComputed(binding: b))
  ]))

// Step 3: pay cost. Dispatches on normal-Aether vs off-turn-Debt vs free-copy.
Game.abilities += Triggered(
  on:     Event.PlayCard.CostComputed(binding: b),
  effect: Cond([
    // Copy-effect plays are cost-free.
    (b.play_source == from_copy,
      EmitEvent(Event.PlayCard.CostPaid(binding: b))),

    // Off-turn Interrupt: accrue Debt = 2 * PRINTED cost (R-5).
    (has_keyword(b.card, Interrupt) ∧ Game.active_player != b.player,
      Sequence([
        Guard(
          predicate:
            b.player.debt + 2 * b.card.printed_cost
              <= next_rise_refresh(b.player),       // see R-6 / §4.5.5
          on_fail: AbortPlay(b, reason: debt_exceeds_next_refresh)),
        IncCounter(b.player, debt, 2 * b.card.printed_cost),
        EmitEvent(Event.PlayCard.CostPaid(binding: b))
      ])),

    // Normal play: pay Aether directly.
    (Default,
      Sequence([
        Guard(
          predicate: b.player.aether >= b.effective_cost,
          on_fail:   AbortPlay(b, reason: insufficient_aether)),
        PayAether(b.player, b.effective_cost),
        EmitEvent(Event.PlayCard.CostPaid(binding: b))
      ]))
  ]))

// Step 4: bind tiers against the current Resonance Field (card not yet Pushed).
Game.abilities += Triggered(
  on:     Event.PlayCard.CostPaid(binding: b),
  effect: Sequence([
    OpenTimingWindow(tier_binding, binding: b),
    SetCharacteristic(b, tier_snapshot,
      EvaluateAllTiers(card: b.card, field: b.player.ResonanceField)),
    EmitEvent(Event.PlayCard.TierBound(binding: b))
  ]))

// Step 4.5: post-tier Interrupt window.
Game.abilities += Triggered(
  on:     Event.PlayCard.TierBound(binding: b),
  effect: Sequence([
    OpenTimingWindow(post_tier_interrupt, binding: b),
    DrainTriggersFor(window: post_tier_interrupt),
    EmitEvent(Event.PlayCard.InterruptWindowClosed(binding: b))
  ]))

// Step 5a: normal resolution when not countered.
Game.abilities += Triggered(
  on:     Event.PlayCard.InterruptWindowClosed(binding: b),
  guard:  b.countered == false,
  effect: Sequence([
    OpenTimingWindow(card_resolving, binding: b),
    ResolveCardByType(b),
    SetCharacteristic(b, resolved, true),
    EmitEvent(Event.PlayCard.Resolved(binding: b))
  ]))

// Step 5b: skip resolution when countered (still proceed to Push).
Game.abilities += Triggered(
  on:     Event.PlayCard.InterruptWindowClosed(binding: b),
  guard:  b.countered == true,
  effect: Sequence([
    // Countered Maneuvers go to caster's Cache without resolving.
    If(b.card.type == Maneuver,
       MoveTo(b.card, b.player.Cache),
       NoOp),
    EmitEvent(Event.PlayCard.Resolved(binding: b))
  ]))

// Card-type dispatch for Step 5a.
define ResolveCardByType(b) = Switch(b.card.type, {
  Unit: Sequence([
    InstantiateUnit(
      card:           b.card,
      owner:          b.player,
      controller:     b.player,
      arena:          b.arena,
      initial_states: { DeploymentSickness: not has_keyword(b.card, Blitz) }),
    ApplyOnEnterTriggers(b.card)
  ]),
  Maneuver: Sequence([
    FireOnResolve(card: b.card, tier: b.tier_snapshot, targets: b.targets),
    MoveTo(b.card, b.player.Cache)
  ]),
  Standard: Sequence([
    InstantiateStandard(
      card:            b.card,
      owner:           b.player,
      controller:      b.player,
      attachment_spec: b.card.subtype,
      attachment_arg:  b.arena_or_player),
    ApplyOnEnterTriggers(b.card)
  ])
})

// Step 6: Push. Fires regardless of b.countered (R-2). Exception: copy-effect
// plays never Push (the original play already did).
Game.abilities += Triggered(
  on:     Event.PlayCard.Resolved(binding: b),
  effect: Sequence([
    If(ShouldPush(b),
       Sequence([
         PushEcho(
           player:   b.player,
           factions: Coalesce(b.push_faction_override, b.card.factions)),
         Append(b.player.pushes_this_turn,
           { card: b.card,
             factions: Coalesce(b.push_faction_override, b.card.factions) })
       ]),
       NoOp),
    EmitEvent(Event.PlayCard.Pushed(binding: b))
  ]))

// Ephemeral binding teardown.
Game.abilities += Triggered(
  on:     Event.PlayCard.Pushed(binding: b),
  effect: DestroyEntity(b))
```

#### 4.5.4 Supporting predicates and effects

```
// A card from hand Pushes unless explicitly marked non-Pushing (Baseline).
// Copies and tokens never Push.
define ShouldPush(b) =
  b.card.pushes_echo == true
  ∧ b.play_source == from_hand

// Counter action: an Interrupt that counters calls this on the target binding.
// It sets countered=true so Step 5a is suppressed. The counter's own
// OnResolve then handles its printed effect (e.g., The Last Whisper's Push
// override) via SetPushOverride on the target binding.
define CounterCard(target_binding) =
  SetCharacteristic(target_binding, countered, true)

define SetPushOverride(target_binding, factions) =
  SetCharacteristic(target_binding, push_faction_override, factions)

// A copy-effect play (Eremis Peak) spawns a fresh binding and jumps the chain.
define PlayAsCopy(player, original_card, targets, arena) = Sequence([
  InstantiateEntity(
    kind:            PlayBinding,
    player:          player,
    card:            original_card,       // same Card entity; characteristics shared
    targets:         targets,
    arena:           arena,
    play_source:     from_copy,
    bind:            copy_b),
  // Jump straight to CostComputed (cost is skipped) — the chain's CostComputed
  // handler sees play_source==from_copy and emits CostPaid unconditionally.
  SetCharacteristic(copy_b, effective_cost, 0),
  EmitEvent(Event.PlayCard.CostComputed(binding: copy_b))
])
```

#### 4.5.5 `next_rise_refresh` — correct Debt cap

```
// The aether refresh the player will receive at their NEXT Rise, before Debt
// deductions. Depends on whether the player is first or second in turn order.
define next_rise_refresh(p) =
  let next_turn_round =
    If(Game.active_player == p,
       // p is mid-turn; their next Rise is next round.
       Game.round + 1,
       // p is non-active; their next Rise is this round if they come after the
       // active player in turn order, otherwise next round.
       If(comes_after_in_turn_order(p, Game.active_player),
          Game.round,
          Game.round + 1))
  in p.aether_cap_schedule[next_turn_round - 1]
```

This replaces the indexing bug in the previous encoding, which used the current round's cap unconditionally.

### 4.6 Clash — Declarative Resolution

Clash is a single Triggered ability on Game. The per-Arena math lives on the Arena entities as derived characteristics (read continuously; recomputed when dependencies change). The Clash phase ability just opens windows, applies damage using those derived values, and closes windows. No simulation loop.

#### 4.6.1 Per-Unit Clash contributions

Each Unit has two derived characteristics that keyword Statics can layer over:

```
Unit.derived += {
  // Force contributed to Projected Force at Clash.
  clash_projected_force: int =
    Cond([
      (self has Phantoming,                                        0),
      (self has DeploymentSickness ∧ not has_keyword(self, Blitz), 0),
      (self has Sentinel,                                          0),
      (Default,                                                    self.force)
    ]),

  // Contribution to Fortification at Clash.
  clash_fortification: int =
    Cond([
      (self has Phantoming, 0),
      (self has Sentinel,   self.force + self.current_ramparts),
      (Default,             self.current_ramparts)
    ])
}
```

Keywords that would alter Clash contributions (Sentinel, Phantom, Deployment Sickness) are read here rather than in Clash's orchestration, so adding a new contribution-altering keyword is a one-line Static on the Unit, not a Clash-procedure patch.

#### 4.6.2 Per-Arena derived values

```
Arena[pos].derived += {
  projected_force[side]: int =
    Sum(u ∈ self.units[side], u.clash_projected_force),

  fortification[side]: int =
    Sum(u ∈ self.units[side], u.clash_fortification)
      + sum_of_fortification_modifiers_from_standards_and_effects(self, side),

  incoming[side]: int =
    Max(0, self.projected_force[other_player(side)] - self.fortification[side])
}
```

The `fortification` summation includes any effect-based Fortification bonuses (e.g., Hold the Line's Prevent-Incoming is a negated bonus applied here — more precisely it's a damage-reduction registered on the Arena for this turn).

#### 4.6.3 The Clash phase ability

```
Game.abilities += Triggered(
  on:     Event.PhaseBegin(phase: Clash, player: active),
  effect: Sequence([
    EmitEvent(Event.Clash.Begin(active_player: active)),

    // Start-of-Clash window: Phantom declarations, last-second Interrupts, etc.
    OpenTimingWindow(start_of_clash),
    DrainTriggersFor(window: start_of_clash),

    // Apply damage to Conduits simultaneously across all Arenas.
    ApplyClashDamage(),
    EmitEvent(Event.Clash.DamageApplied),

    // End-of-Clash window: Phantom returns, cost-reduction accounting, etc.
    OpenTimingWindow(end_of_clash),
    DrainTriggersFor(window: end_of_clash),

    EmitEvent(Event.Clash.End)
  ]))

define ApplyClashDamage() =
  ForEach((arena, side) ∈ {Left, Center, Right} × {Player1, Player2},
    If(Conduit[side, arena] exists ∧ not collapsed_for(side, arena),
       DealDamageToConduit(
         target: Conduit[side, arena],
         amount: Arena[arena].incoming[side],
         source: clash)))
```

#### 4.6.4 Notes on damage events (folded in from old §4.6.1)

- **Clash damage does not touch Unit Ramparts.** Clash Incoming targets the defending Conduit directly; Fortification is absorbed into `incoming` before any damage is dealt.
- **Damage to Units comes only from Maneuvers, abilities, and passive keywords like Ignite.** Those pathways use the general `DealDamage(target, amount)` atom, which reduces `current_ramparts`.
- **Ramparts heal at end of Fall**, not at Rise (see R-4 and the Fall ability in §4.3).

### 4.7 Victory

Victory is SBA-driven (§4.4). No additional machinery needed.

---

## 5. Token Templates

```
Token ThornSapling {
  characteristics: { name: "Thorn Sapling", cost: 0, type: Unit,
                     factions: {NEUTRAL}, force: 1, ramparts: 1, rarity: Token }
  keywords: [ DeploymentSickness ]   // enters with sickness; Saplings gain Force next turn
  pushes_echo: false
  abilities: []
}
```

---

## 6. Cards

Card encoding template:

```
Card <id> {
  name:       "..."
  factions:   { ... }
  type:       Unit | Maneuver | Standard
  subtype:    <attachment spec for Standards; else none>
  cost:       <int>
  force:      <int>         // Units only
  ramparts:   <int>         // Units only
  rarity:     C | U | R | M
  unique:     true | false
  keywords:   [ ... ]
  abilities:  [ ... ]
  // text:    original English from Supplement
}
```

### 6.1 EMBER

```
Card Cinderling {
  factions: {EMBER}, type: Unit, cost: 1, force: 2, ramparts: 1, rarity: C
  keywords: [ Blitz, DeploymentSickness ]   // DeploymentSickness is universal; Blitz suppresses it
  abilities: []
  // text: Blitz.
}

Card Spark {
  factions: {EMBER}, type: Maneuver, cost: 1, rarity: C
  abilities: [
    OnResolve(
      Target(t -> t.kind ∈ {Unit, Conduit},
        Tiers(
          (Resonance(EMBER, 3), DealDamage(target, 3)),
          (True,                DealDamage(target, 2))
        )))
  ]
  // text: Deal 2 damage to a Unit or Conduit. EMBER 3: Deal 3 instead.
}

Card EmberhandRaider {
  factions: {EMBER}, type: Unit, cost: 2, force: 3, ramparts: 2, rarity: C
  keywords: [ Surge, DeploymentSickness ]
  abilities: []
  // text: Surge.
}

Card Smolder {
  factions: {EMBER}, type: Maneuver, cost: 2, rarity: C
  abilities: [
    OnResolve(
      Target(t -> t.kind ∈ {Unit, Conduit},
        Tiers(
          (Resonance(EMBER, 4), DealDamage(target, 4)),
          (True,                DealDamage(target, 2))
        )))
  ]
  // text: Deal 2 damage to a target Unit or Conduit. EMBER 4: Deal 4 instead.
}

Card Pyrebrand {
  factions: {EMBER}, type: Unit, cost: 3, force: 4, ramparts: 2, rarity: U
  keywords: [ Blitz, DeploymentSickness, Ignite(1) ]
  abilities: [
    // EMBER 3: Ignite 2 while this is in play — an ongoing conditional upgrade.
    Static(
      modifies: KeywordParameter(self, Ignite),
      check_at: continuously,
      rule: If(Resonance(EMBER, 3), set X = 2, leave X = 1))
  ]
  // text: Blitz. Ignite 1. EMBER 3: Ignite 2 while this is in play.
}

Card Stokefire {
  factions: {EMBER}, type: Maneuver, cost: 3, rarity: U
  abilities: [
    OnResolve(
      Tiers(
        (Peak(EMBER),   DistributeDamage(amount: 5, chooser: self.controller,
                                         target_filter: t -> t.kind ∈ {Unit, Conduit})),
        (True,          DistributeDamage(amount: 3, chooser: self.controller,
                                         target_filter: t -> t.kind ∈ {Unit, Conduit}))
      ))
  ]
  // text: Deal 3 damage, split among any number of targets. Peak EMBER: Deal 5, split as you choose.
}

Card ChoirOfTheBlaze {
  factions: {EMBER}, type: Standard, subtype: attaches_to_arena, cost: 4, rarity: R
  abilities: [
    StartOfYourTurn(
      Sequence([
        ForEach(
          u -> u.arena == self.arena
             ∧ u.controller != self.controller
             ∧ u.type == Unit,
          DealDamage(u, 1)),
        When(Resonance(EMBER, 4),
          If(Conduit[other_player(self.controller), self.arena] exists
             ∧ not collapsed_for(other_player(self.controller), self.arena),
             DealDamage(Conduit[other_player(self.controller), self.arena], 1),
             NoOp),
          check_at: event_emission,
          apply_at: after_event)
      ]))
  ]
  // text: At start of your turn, deal 1 damage to each opposing Unit in this Arena.
  //       EMBER 4: Also deal 1 damage to the opposing Conduit in this Arena.
}

Card Eremis_TheUnfaded {
  factions: {EMBER}, type: Unit, cost: 5, force: 5, ramparts: 3, rarity: M, unique: true
  keywords: [ Blitz, DeploymentSickness, Unique ]
  abilities: [
    // Peak EMBER: each EMBER Maneuver you play is copied; each card copyable once per turn.
    // `once_per_turn_per_trigger_subject: event.card` tells the harness to gate by the
    // triggering card identity, resetting at the controller's next Rise.
    OnCardPlayed(
      filter:                              c -> c.type == Maneuver ∧ EMBER ∈ c.factions,
      once_per_turn_per_trigger_subject:   event.card,
      effect: When(Peak(EMBER),
        PlayAsCopy(
          player:        self.controller,
          original_card: event.card,
          targets:       event.targets,
          arena:         event.arena),
        check_at: announcement,
        apply_at: after_event))
  ]
  // text: Blitz. Peak EMBER: When you play an EMBER Maneuver, copy it. Each EMBER Maneuver
  //       can only be copied this way once per turn.
  //       (PlayAsCopy is defined in §4.5.4 — copies bypass cost and Push.)
}
```

### 6.2 BULWARK

```
Card Palisade {
  factions: {BULWARK}, type: Unit, cost: 1, force: 0, ramparts: 3, rarity: C
  keywords: [ Sentinel, DeploymentSickness ]
  abilities: []
  // text: Sentinel.
}

Card WardenInitiate {
  factions: {BULWARK}, type: Unit, cost: 2, force: 2, ramparts: 3, rarity: C
  keywords: [ Fortify(1), DeploymentSickness ]
  abilities: []
  // text: Fortify 1.
}

Card SealTheBreach {
  factions: {BULWARK}, type: Maneuver, cost: 2, rarity: C
  abilities: [
    OnResolve(
      Target(t -> t.kind == Conduit,
        Tiers(
          (Resonance(BULWARK, 3), Heal(target, 5, cap: starting_integrity)),
          (True,                  Heal(target, 3, cap: starting_integrity))
        )))
  ]
  // text: Heal target Conduit 3. BULWARK 3: Heal 5 instead.
}

Card HoldTheLine {
  factions: {BULWARK}, type: Maneuver, cost: 2, rarity: C
  keywords: [ Interrupt ]
  abilities: [
    OnResolve(
      Target(a -> a.kind == Arena,
        PreventIncoming(arena: target, side: self.controller,
                        amount_cap: 4, duration: until_end_of_turn)))
  ]
  // text: Prevent up to 4 Incoming in one Arena this turn.
}

Card CohortCaptain {
  factions: {BULWARK}, type: Unit, cost: 3, force: 3, ramparts: 4, rarity: U
  keywords: [ DeploymentSickness ]
  abilities: [
    // Static ongoing grant: another BULWARK Unit entering this Arena gets Fortify 1 while CC is in play.
    OnArenaEnter(
      filter: u -> BULWARK ∈ u.factions ∧ u.type == Unit ∧ u.controller == self.controller,
      effect: GrantKeyword(target, Fortify(1), duration: while_source_in_play(self)))
  ]
  // text: When another BULWARK Unit enters this Arena, it gains Fortify 1 while Cohort Captain is in play.
}

Card Reconstitute {
  factions: {BULWARK}, type: Maneuver, cost: 4, rarity: U
  abilities: [
    OnResolve(
      Target(c -> c.kind == Conduit ∧ c.owner == self.controller,
        If(not collapsed_for(self.controller, target.arena),
           Heal(target, amount: starting_integrity_of(target), cap: starting_integrity),
           // Un-collapse: restore Conduit at 3 integrity, clear Collapsed flag.
           Sequence([
             SetCounter(target, integrity, 3),
             SetFlag(Arena[target.arena].collapsed_for[self.controller], false)
             // Units lost at collapse are NOT returned (explicit in card text).
           ]))))
  ]
  // text: Heal target friendly Conduit to starting Integrity. If Conduit is destroyed, restore
  //       at 3 Integrity and un-Collapse your side. Units lost are not returned.
}

Card TheQuietWall {
  factions: {BULWARK}, type: Standard, subtype: attaches_to_player, cost: 5, rarity: R
  abilities: [
    // Global: all controller's Units gain Sentinel (layer 3).
    Static(
      modifies: KeywordSet(unit),
      check_at: continuously,
      rule: ForEach(u -> u.controller == self.controller ∧ u.type == Unit,
              GrantKeyword(u, Sentinel))),
    // Peak BULWARK: Units project 1 Force each at Clash despite Sentinel.
    Static(
      modifies: ClashContribution(unit),
      check_at: continuously,
      rule: When(Peak(BULWARK),
        ForEach(u -> u.controller == self.controller ∧ u.type == Unit,
          OverrideContribution(u, projected_force: 1))))
  ]
  // text: All your Units have Sentinel. Peak BULWARK: Your Units Project 1 Force each at Clash despite Sentinel.
}

Card Vaen_ArchitectOfTheLastHour {
  factions: {BULWARK}, type: Unit, cost: 6, force: 2, ramparts: 7, rarity: M, unique: true
  keywords: [ Mend(3), Sentinel, Unique, DeploymentSickness ]
  abilities: [
    // BULWARK 4: while this tier is satisfied, friendly Units in Vaen's Arena have +2 Ramparts.
    Static(
      modifies: Characteristic(target_unit, ramparts, layer: 2),
      check_at: continuously,
      rule: When(Resonance(BULWARK, 4),
        ForEach(u -> u.arena == self.arena
                   ∧ u.controller == self.controller
                   ∧ u.type == Unit,
          add(+2)))),
    // Peak BULWARK: Conduits cannot be reduced below 1 Integrity by opposing effects.
    // Lost if Vaen leaves play (ability attached to Vaen, so naturally deregisters).
    Static(
      modifies: DamageReduction(Conduit, by_opposing),
      check_at: continuously,
      rule: When(Peak(BULWARK),
        ForEach(c -> c.kind == Conduit ∧ c.owner == self.controller,
          Floor(c.integrity, min: 1, source_filter: e -> e.controller != self.controller))))
  ]
  // text: Mend 3. Sentinel. BULWARK 4: while satisfied, friendly Units in Vaen's Arena
  //       have +2 Ramparts. Peak BULWARK: Your Conduits cannot be reduced below 1 Integrity
  //       by opposing effects.
}
```

### 6.3 TIDE

```
Card Ripplekin {
  factions: {TIDE}, type: Unit, cost: 1, force: 1, ramparts: 2, rarity: C
  keywords: [ Drift, DeploymentSickness ]
  abilities: []
  // text: Drift.
}

Card Refract {
  factions: {TIDE}, type: Maneuver, cost: 1, rarity: C
  abilities: [
    OnResolve(
      Target(u -> u.type == Unit,
        MoveTo(target, target.owner.Hand)))
  ]
  // text: Return target Unit (yours or opponent's) to its owner's hand.
}

Card Brinescribe {
  factions: {TIDE}, type: Unit, cost: 3, force: 2, ramparts: 3, rarity: C
  keywords: [ Drift, DeploymentSickness ]
  abilities: [
    OnEnter(Draw(self.controller, 1))
  ]
  // text: When you play this, draw a card. Drift.
}

Card ReshapeTheCurrent {
  factions: {TIDE}, type: Maneuver, cost: 2, rarity: C
  abilities: [
    // Reshape is announcement-window; applies before the Push at step 6.
    OnResolve(Reshape(2))
  ]
  // text: Reshape twice.
}

Card Harborkeeper {
  factions: {TIDE}, type: Unit, cost: 3, force: 3, ramparts: 3, rarity: U
  keywords: [ DeploymentSickness ]
  abilities: [
    // Redirect-to-adjacent replacement ability.
    Replacement(
      on:    Event.EnterPlay(target=u),
      guard: u.controller == self.controller
           ∧ u.type == Unit
           ∧ u.intended_arena is collapsed_for(self.controller)
           ∧ exists a ∈ adjacent(u.intended_arena) with not collapsed_for(self.controller, a),
      replace_with:
        Target(a -> a ∈ adjacent(u.intended_arena) ∧ not collapsed_for(self.controller, a),
          chooser: self.controller,
          effect:  Event.EnterPlay(target=u, arena=a)))
  ]
  // text: When another of your Units would enter a Collapsed Arena of yours, it may enter
  //       an adjacent non-Collapsed Arena of yours instead.
}

Card Undertow {
  factions: {TIDE}, type: Maneuver, cost: 3, rarity: U
  abilities: [
    OnResolve(
      Target(a -> a.kind == Arena,
        Tiers(
          (Resonance(TIDE, 4),
            // Any reassignment: for each Unit in target arena, choose a destination Arena.
            ForEach(u -> u.arena == target,
              Target(dest -> dest.kind == Arena ∧ not collapsed_for(u.controller, dest),
                chooser: self.controller,
                effect: MoveUnitToArena(u, dest)))),
          (True,
            // Shift all left or right (per Unit choice), edge stays.
            ForEach(u -> u.arena == target,
              Target(dir -> dir ∈ {left, right},
                chooser: self.controller,
                effect: If(exists neighbor = shift(u.arena, dir),
                           MoveUnitToArena(u, neighbor),
                           NoOp))))
        )))
  ]
  // text: Move each Unit in target Arena one Arena left or right. Edge-stops stay.
  //       TIDE 4: Instead, choose any reassignment.
}

Card TheStandingWave {
  factions: {TIDE}, type: Standard, subtype: attaches_to_player, cost: 4, rarity: R
  abilities: [
    // When the controller plays a card, may Reshape once.
    OnCardPlayed(
      filter: c -> c.controller == self.controller,
      effect: Reshape(1)),
    // Peak TIDE: also may move one Unit to a new Arena.
    OnCardPlayed(
      filter: c -> c.controller == self.controller,
      effect: When(Peak(TIDE),
        Choice(self.controller, {
          NoOp,
          Target(u -> u.controller == self.controller ∧ u.type == Unit,
            Target(a -> a.kind == Arena ∧ not collapsed_for(self.controller, a),
              MoveUnitToArena(u, a)))
        })))
  ]
  // text: When you play a card, you may Reshape once. Peak TIDE: also may move one Unit to a new Arena.
}

Card Thessa_WhoReturns {
  factions: {TIDE}, type: Unit, cost: 5, force: 3, ramparts: 4, rarity: M, unique: true
  keywords: [ Drift, Recur, Unique, DeploymentSickness ]
  abilities: [
    // TIDE 3: once per turn, when you play a TIDE card, may return a non-Unique TIDE card
    //         from your Cache to your hand. `once_per_turn: true` tells the Rise reset
    //         (§4.3) to clear this ability's `used_this_turn` counter automatically; the
    //         harness also gates the effect body from firing twice in the same turn.
    OnCardPlayed(
      filter:        c -> c.controller == self.controller ∧ TIDE ∈ c.factions,
      once_per_turn: true,
      effect: When(Resonance(TIDE, 3),
        Choice(self.controller, {
          pass: NoOp,
          take: Target(card -> card ∈ self.controller.Cache
                             ∧ TIDE ∈ card.factions
                             ∧ not (Unique ∈ card.keywords),
                  MoveTo(target, self.controller.Hand))
        }),
        check_at: announcement,
        apply_at: after_event))
  ]
  // text: Drift. Recur. TIDE 3: When you play a TIDE card, you may return a non-Unique
  //       TIDE card from your Cache to your hand. Once per turn.
}
```

### 6.4 THORN

```
Card Thornling {
  factions: {THORN}, type: Unit, cost: 1, force: 1, ramparts: 1, rarity: C
  keywords: [ Rally, DeploymentSickness ]
  abilities: []
  // text: Rally.
}

Card TakeRoot {
  factions: {THORN}, type: Maneuver, cost: 2, rarity: C
  abilities: [
    OnResolve(
      // Context: Sprawl X operates against self.arena, but a Maneuver has no arena.
      // Author intent: Sprawl in an arena the controller chooses.
      Target(a -> a.kind == Arena ∧ not collapsed_for(self.controller, a),
        ForEach(i -> i in [1..2],
          CreateToken(template: ThornSapling,
                      zone: a,
                      owner: self.controller,
                      controller: self.controller))))
  ]
  // text: Sprawl 2.
}

Card GroveKinDruid {
  factions: {THORN}, type: Unit, cost: 2, force: 2, ramparts: 2, rarity: C
  keywords: [ DeploymentSickness ]
  abilities: [
    OnEnter(
      ForEach(u -> u.arena == self.arena
                 ∧ u.controller == self.controller
                 ∧ u.type == Unit,
        ModifyCharacteristic(u, ramparts, +1, duration: end_of_turn, layer: 2)))
  ]
  // text: When you play this, friendly Units in this Arena gain +1 Ramparts until end of turn.
}

Card KindlingBrazier {
  factions: {THORN}, type: Standard, subtype: attaches_to_arena, cost: 2, rarity: C
  keywords: [ Kindle(effect: Sprawl(1, in: self.arena, for: self.controller)) ]
  abilities: []
  // text: Kindle: Sprawl 1. (Every 3rd start-of-turn, create a 1/1 Sapling in this Arena.)
}

Card Thicketwarden {
  factions: {THORN}, type: Unit, cost: 3, force: 3, ramparts: 4, rarity: U
  keywords: [ DeploymentSickness ]
  abilities: [
    // Conditional +2 Force while 3+ friendly Units share this Arena.
    Static(
      modifies: Characteristic(self, force, layer: 2),
      check_at: continuously,
      rule: If(Count({u : u.arena == self.arena
                        ∧ u.controller == self.controller
                        ∧ u.type == Unit}) >= 3,
               add(+2),
               no change))
  ]
  // text: While there are 3+ friendly Units in this Arena, this Unit has +2 Force.
}

Card Bloom {
  factions: {THORN}, type: Maneuver, cost: 3, rarity: U
  abilities: [
    // Counts Echoes directly; no tier gate. Uses CountEcho macro.
    OnResolve(
      Target(a -> a.kind == Arena ∧ not collapsed_for(self.controller, a),
        ForEach(i -> i in [1..CountEcho(THORN)],
          CreateToken(template: ThornSapling, zone: a,
                      owner: self.controller, controller: self.controller))))
  ]
  // text: For each THORN Echo in your Resonance Field, create a 1/1 Sapling in an Arena of your choice.
}

Card TheEndlessThicket {
  factions: {THORN}, type: Standard, subtype: attaches_to_player, cost: 5, rarity: R
  abilities: [
    StartOfYourTurn(
      Tiers(
        (Peak(THORN),
          // Every arena, regardless of unit count; Saplings enter with +1/+0.
          ForEach(a -> a.kind == Arena ∧ not collapsed_for(self.controller, a),
            CreateToken(template: ThornSapling, zone: a,
                        owner: self.controller, controller: self.controller,
                        with_modifier: Characteristic(force, +1, layer: 2)))),
        (True,
          ForEach(a -> a.kind == Arena
                     ∧ not collapsed_for(self.controller, a)
                     ∧ Count({u : u.arena == a
                                ∧ u.controller == self.controller
                                ∧ u.type == Unit}) >= 2,
            CreateToken(template: ThornSapling, zone: a,
                        owner: self.controller, controller: self.controller)))
      ))
  ]
  // text: At start of your turn, Sprawl 1 in each of your Arenas with 2+ friendly Units.
  //       Peak THORN: Sprawl in every Arena regardless; Saplings enter with +1/+0.
}

Card Myrrhan_KeeperOfGrowth {
  factions: {THORN}, type: Unit, cost: 5, force: 4, ramparts: 5, rarity: M, unique: true
  keywords: [ Rally, Unique, DeploymentSickness ]
  abilities: [
    // THORN 3: your Saplings are 2/2 instead of 1/1 (global stat buff).
    Static(
      modifies: Characteristic(sapling_tokens, {force, ramparts}, layer: 1),
      check_at: continuously,
      rule: When(Resonance(THORN, 3),
        ForEach(s -> s.template == ThornSapling ∧ s.controller == self.controller,
          SetBase(s, force: 2, ramparts: 2)))),
    // Peak THORN: at start of turn, Sprawl 2 in each Arena you have a Unit in.
    StartOfYourTurn(
      When(Peak(THORN),
        ForEach(a -> a.kind == Arena
                   ∧ not collapsed_for(self.controller, a)
                   ∧ exists u: u.arena == a ∧ u.controller == self.controller ∧ u.type == Unit,
          ForEach(i -> i in [1..2],
            CreateToken(template: ThornSapling, zone: a,
                        owner: self.controller, controller: self.controller)))))
  ]
  // text: Rally. THORN 3: Your Saplings are 2/2 instead of 1/1.
  //       Peak THORN: At start of your turn, Sprawl 2 in each Arena you have a Unit in.
}
```

### 6.5 HOLLOW

```
Card Veilslip {
  factions: {HOLLOW}, type: Unit, cost: 1, force: 2, ramparts: 1, rarity: C
  keywords: [ Phantom, DeploymentSickness ]
  abilities: []
  // text: Phantom.
}

Card PrivateThought {
  factions: {HOLLOW}, type: Maneuver, cost: 1, rarity: C
  abilities: [
    OnResolve(
      Sequence([
        Reveal(self.controller, opponent_of(self.controller).Hand, count: 2),
        Target(c -> c ∈ revealed_set,
          chooser: self.controller,
          effect: MoveTo(target, bottom_of(opponent_of(self.controller).Arsenal),
                         visibility: exiled))
      ]))
  ]
  // text: Opponent reveals two cards from their hand; you exile one to the bottom of their Arsenal.
}

Card Greyface {
  factions: {HOLLOW}, type: Unit, cost: 2, force: 2, ramparts: 2, rarity: C
  keywords: [ Shroud, DeploymentSickness ]
  abilities: []
  // text: Shroud.
}

Card Unmake {
  factions: {HOLLOW}, type: Maneuver, cost: 3, rarity: C
  abilities: [
    OnResolve(
      Target(u -> u.type == Unit ∧ u.force <= force_threshold(),
        with force_threshold := Tiers(
          (Resonance(HOLLOW, 3), return 4),
          (True,                 return 3)),
        effect: Destroy(target)))
  ]
  // text: Destroy target Unit with Force 3 or less. HOLLOW 3: Force 4 or less instead.
}

Card TheLastWhisper {
  factions: {HOLLOW}, type: Maneuver, cost: 3, rarity: U
  keywords: [ Interrupt ]
  abilities: [
    // Target is a PlayBinding currently inside the post_tier_interrupt window
    // (i.e., a Maneuver that has had its cost paid and tiers bound, but not yet
    // resolved). We counter it and override its Push to HOLLOW. Per R-2 the
    // countered card still Pushes at Step 6 of its play chain.
    OnResolve(
      Target(
        selector: b -> b.kind == PlayBinding
                     ∧ b.card.type == Maneuver
                     ∧ b.countered == false
                     ∧ in_post_tier_interrupt_window(b),
        chooser:  self.controller,
        effect:   Sequence([
          CounterCard(b),                                      // §4.5.4
          SetPushOverride(b, factions: {HOLLOW})               // §4.5.4
        ])))
  ]
  // text: Counter target Maneuver. Its caster Pushes a HOLLOW Echo instead of its printed faction.
  // (Debt cost when opponent-turn: 6. See §4.5 play protocol.)
}

Card BlankfaceCultist {
  factions: {HOLLOW}, type: Unit, cost: 3, force: 3, ramparts: 3, rarity: U
  keywords: [ Phantom, DeploymentSickness ]
  abilities: [
    // When this Phantom-returns to hand (specifically via the Phantom effect at End of Clash),
    // Pilfer 1.
    Triggered(
      on:     Event.PhantomReturn(target=self),
      effect: Pilfer(1))
  ]
  // text: Phantom. When this returns to your hand via Phantom, Pilfer 1.
}

Card TheUnreadPage {
  factions: {HOLLOW}, type: Standard, subtype: attaches_to_opponent, cost: 4, rarity: R
  abilities: [
    // Opponent chooses: hand_cap := 5, or aether_refresh -= 1.
    // Peak HOLLOW: both apply, no choice.
    OnEnter(
      If(Peak(HOLLOW),
         // Both apply from now until this leaves play.
         Sequence([
           SetModifier(opponent_of(self.controller), hand_cap, value: 5, while_source: self),
           SetModifier(opponent_of(self.controller), aether_refresh_delta, value: -1, while_source: self)
         ]),
         // Opponent chooses at enter-play; choice is locked.
         Choice(opponent_of(self.controller), {
           SetModifier(opponent_of(self.controller), hand_cap, value: 5, while_source: self),
           SetModifier(opponent_of(self.controller), aether_refresh_delta, value: -1, while_source: self)
         }))),
    // If Peak status changes later, re-evaluate to force both.
    Static(
      modifies: ActiveModifiers(self),
      check_at: continuously,
      rule: If(Peak(HOLLOW),
        EnsureModifiers({hand_cap_5, aether_minus_1})))
  ]
  // text: Your opponent chooses: max hand size 5, or starting Aether -1. Peak HOLLOW: both apply.
}

Card Oreth_TheUnseenAuthor {
  factions: {HOLLOW}, type: Unit, cost: 6, force: 4, ramparts: 3, rarity: M, unique: true
  keywords: [ Shroud, Phantom, Unique, DeploymentSickness ]
  abilities: [
    OnEnter(
      When(Resonance(HOLLOW, 4),
        Pilfer(2))),
    Triggered(
      on:     Event.PhantomReturn(target=self),
      effect: When(Peak(HOLLOW),
        RandomExileFromHand(opponent_of(self.controller), count: 1)))
  ]
  // text: Shroud. Phantom. HOLLOW 4: When Oreth enters play, Pilfer 2.
  //       Peak HOLLOW: When Oreth returns to your hand via Phantom, opponent exiles a card from hand at random.
}
```

### 6.6 Dual-Faction

```
Card AshrootWhelp {
  factions: {EMBER, THORN}, type: Unit, cost: 2, force: 2, ramparts: 2, rarity: U
  keywords: [ Blitz, Rally, DeploymentSickness ]
  abilities: []
  // text: Blitz. Rally. (Pushes both EMBER and THORN.)
  // Push semantics: single Echo with factions = {EMBER, THORN}, counts once toward each.
}

Card TidecallersHollow {
  factions: {TIDE, HOLLOW}, type: Maneuver, cost: 3, rarity: R
  abilities: [
    OnResolve(
      Target(u -> u.type == Unit,
        Sequence([
          MoveTo(target, target.owner.Hand),
          RandomExileFromHand(target.owner, count: 1)
        ])))
  ]
  // text: Return target Unit to its owner's hand, then that player exiles a card from their hand at random.
}
```

### 6.7 Neutral

```
Card Baseline {
  factions: {NEUTRAL}, type: Unit, cost: 1, force: 1, ramparts: 1, rarity: C
  pushes_echo: false   // format-specific: Baseline explicitly does not Push, even a Neutral Echo
  keywords: [ DeploymentSickness ]
  abilities: []
  // text: (Draft-only filler. Does not Push any Echo.)
}

Card ConduitTender {
  factions: {NEUTRAL}, type: Unit, cost: 2, force: 1, ramparts: 3, rarity: C
  keywords: [ Mend(1), DeploymentSickness ]
  abilities: []
  // text: Mend 1.
}

Card ScavengedBlade {
  factions: {NEUTRAL}, type: Maneuver, cost: 2, rarity: C
  abilities: [
    OnResolve(
      Target(u -> u.type == Unit,
        If(BannerExists(),
           DealDamage(target, 3),
           DealDamage(target, 2),
           check_at: tier_binding,
           apply_at: card_resolving)))
  ]
  // text: Deal 2 damage to a Unit. If your Banner exists: Deal 3 instead.
}

Card Wayfarer {
  factions: {NEUTRAL}, type: Unit, cost: 3, force: 3, ramparts: 3, rarity: U
  keywords: [ DeploymentSickness ]
  abilities: [
    OnEnter(
      If(BannerExists(),
         Draw(self.controller, 1),
         NoOp))
  ]
  // text: When you play this, if your Banner exists, draw a card.
}
```

---

## 7. Notes on the Encoding

### 7.1 What fell out cleanly

- Every keyword is a macro in §3.4.
- Every tier-table card is `Tiers(...)`.
- Every Banner-conditional is `When(Banner(F), ...)` or `If(BannerExists(), ...)`.
- Every "counts echoes" card (Bloom, Myrrhan's Sapling buff, The Endless Thicket Peak) reads the Resonance Field directly via `CountEcho(F)` or `Resonance(F, N)` — no separate mechanism.
- The play chain (§4.5) and Clash resolution (§4.6) are now fully event- and characteristic-driven. No remaining procedures in §4; the former "Procedure PlayCard" and "Procedure ResolveClash" are both sequences of Triggered abilities on `Game` plus derived characteristics on `Unit` and `Arena`.
- Setup (§4.2) is a single Triggered ability on Game, composed of named sub-effect macros. Mulligan is a `Choice` inside a `Repeat`, no control-flow primitive invented.
- Per-Unit Clash contribution lives on the Unit itself (§4.6.1) as two derived characteristics. A new contribution-altering keyword adds a Static to the Unit; Clash orchestration never needs to change.

### 7.2 Bugs fixed in this revision

1. Removed the invented "turn-1 first player skips draw" Replacement ability. GameRules §4 makes second-player's draw-6 the sole compensation.
2. Debt now uses `printed_cost`, not `effective_cost`, per R-5.
3. Debt cap now uses `next_rise_refresh(p)` per R-6, which correctly indexes the player's next Rise regardless of who is first/second.
4. Removed the duplicate Rampart reset from Rise; healing lives only at end of Fall per R-4.
5. Thessa's and Eremis's once-per-turn tracking now uses the harness's `once_per_turn` (and `once_per_turn_per_trigger_subject` for Eremis) ability annotations, not hand-rolled counters.
6. Collapse SBA now uses the proper type expression for Arena-attached Standards and emits `Event.Destroy(reason: conduit_collapse)` so Recur can intercept (per R-1).
7. The Last Whisper now targets a `PlayBinding` entity (the proper addressable thing in the new play chain) rather than a loose "pending Maneuver" reference.

### 7.3 Remaining subtleties worth a playtest

1. **"When you play a card" + Reshape.** The Standing Wave's Reshape fires at `announcement`, i.e., before tier binding. A well-timed Reshape can thus retroactively change which tier a card lands on. Intended, but surprising to new players.

2. **Harborkeeper's "would enter."** Encoded as a Replacement on `Event.EnterPlay(target=u)`. Requires the harness to treat EnterPlay as a *pending* event with a mutable `arena` field that Replacements can rewrite before commit. This is standard would-event plumbing; call it out in the harness spec.

3. **The Unread Page + Peak flapping.** Current encoding: opponent's choice is locked at Enter-Play. If Peak is reached later, a Static layers both modifiers on. If Peak is then lost, the Static stops applying its forced modifier, but the opponent's original choice is still active. If Peak returns, the Static re-applies. Check with playtest whether "once both modifiers have been forced, opponent loses the ability to revert to just one" is the intended feel.

4. **Eremis's copy semantics.** Handled cleanly by `PlayAsCopy` in §4.5.4: the copy is a fresh PlayBinding with `play_source: from_copy`, which skips cost entirely and fails `ShouldPush`. No ad-hoc "does this Push?" logic anywhere else.

### 7.4 Deferred schema choices (still open)

- **PhantomReturn as an Event type.** Encoded as distinct so Blankface and Oreth can trigger cleanly. The alternative — a `MoveTo(target → Hand, reason: phantom_return)` with triggers filtering on the reason tag — is equivalent in semantics but requires all move-to-hand triggers to be reason-aware.
- **Implicit DeploymentSickness on all Units.** Currently every Unit card lists `DeploymentSickness` in its keywords array. Cleaner would be to move it to a Game-level Triggered ability that fires on every `Event.EnterPlay(target.type == Unit)` and relies on Blitz's suppression to override. Either choice is sound; explicit is more lintable, implicit is less noisy.
- **Aether schedule on Player vs. Game.** Hardcoded on Player currently. Moving to a Game characteristic enables future format overrides (e.g., "fast mode": [4,5,6,7,8,9,10]).
- **`used_this_turn` vs `used_this_turn_per_subject`.** Two gating modes now exist in the harness vocabulary: blanket (Thessa) and per-trigger-subject (Eremis). The harness should document these as first-class annotations. No other card in the current set needs a third mode.

---

*End of encoding. If any card or rule needs expansion, re-derivation from primitives, or cross-translation back to the other game's encoding, the macro library is the right place to start.*
