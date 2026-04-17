# RESONANCE — CCG-NF Encoding

*A full normal-form encoding of the Resonance CCG, companion to `GameRules.md` and `Supplement.md`. Everything in the game (players, phases, zones, state-based actions, keywords, cards) is expressed in the same six primitives and a small macro library on top.*

---

## 0. Conventions

**Syntax.** YAML-ish for entity declarations (name-value pairs and nested blocks). S-expression-ish for abilities and effects (`Kind(arg: value, ...)`, nested). Macros declared with `define NAME(params) = body` and applied by textual substitution.

**Identifiers.** `self` inside an ability body is always the Entity bearing the ability. `event` is the triggering Event. `target` is the bound Target of the ability's Target binding. `self.controller`, `self.owner`, `self.arena` are Entity attributes.

**Comments.** `//` line comments are non-semantic.

**Natural text.** For every card I include its original English rules text as a `// text:` comment so the encoding is reviewable against the source.

---

## 1. Schema Reference

Six primitives:

- **Entity** — identified bundle of Characteristics, DerivedCharacteristics, Counters, Tags, a Zone, owner/controller, and Abilities. Kinds: Card, Player, Zone, Phase, Game, Arena, Conduit, Echo, Token, Standard, ContinuousModifier.
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
windows := { announcement, cost_payment, tier_binding, resolution,
             start_of_turn, end_of_turn, start_of_clash, end_of_clash,
             event_emission, event_pending, event_commit, after_event,
             activation_declared, after_cost_paid, card_resolving,
             continuously, immediate }
```

**Triggered-vs-Replacement test.** If the triggering event still occurs in full after the ability fires → **Triggered**. If the event is replaced or prevented → **Replacement**.

**Guards re-checked at apply?** Default: no. Guard is checked at `check_at` only. To re-check at apply (the MTG "intervening-if" pattern), annotate `recheck: true`.

**Resonance tier binding is explicit.** Tiers evaluate at `tier_binding` (play protocol step 4, before Push). The result is stored in the card's `resolution_context.tier_snapshot`. The effect at `card_resolving` (step 5) reads that snapshot, *not* the live ResonanceField. This matters when Interrupts fire between steps 4 and 5 and alter the Field — the snapshot is frozen.

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

```
Procedure Setup():
  1. Players present Arsenals (§13 of GameRules).
  2. RandomChoose first_player ∈ {Player1, Player2}.
     Set Game.first_player.
  3. For each Player p, for each arena ∈ {Left, Center, Right}:
       Instantiate Conduit[p, arena] with integrity = 7.
  4. For each Player p: Shuffle(p.Arsenal).
     Draw(Game.first_player, 5).
     Draw(other_player(Game.first_player), 6).
  5. Repeat up to 2 times per player, in turn order starting with first_player:
       Player p may Mulligan(k) where k >= 1:
         - p chooses k cards from Hand.
         - p places them on bottom of p.Arsenal in chosen order (no shuffle).
         - Draw(p, k - 1).   // may be 0 or negative; if negative, simply no draw
  6. Set Game.round := 1.
  7. Begin first_player's Rise phase.
```

### 4.3 Turn Structure (Abilities on the Game)

A turn is a sequence of five Phase entities. Transitions are Events emitted by the Game entity. Rules are Triggered abilities on Game.

```
// Rise phase
Game.abilities += Triggered(
  on:     Event.PhaseBegin(phase=Rise, player=p),
  effect: Sequence([
    // Aether refresh, adjusted by Debt.
    RefillAether(p,
      amount: clamp(p.aether_cap_schedule[Game.round - 1] - p.debt, min: 0)),
    SetCounter(p, debt, 0),

    // Ramparts reset to current maximum for every Unit controlled by p.
    ForEach(u -> u ∈ permanents_of(p) ∧ u.type == Unit,
      SetCounter(u, current_ramparts, max_ramparts_of(u))),

    // Reset once-per-turn activation flags on p's permanents.
    ForEach(a -> a ∈ abilities_of_permanents(p) ∧ a has once_per_turn,
      ClearCounter(a, used_this_turn)),

    // Draw; empty Arsenal => lose.
    If(|p.Arsenal| == 0,
       EmitEvent(Event.Lose(player=p, reason: "deck out")),
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

// Fall phase
Game.abilities += Triggered(
  on: Event.PhaseBegin(phase=Fall, player=p),
  effect: Sequence([
    OpenTimingWindow(end_of_turn, owner=p),
    DrainTriggersFor(window=end_of_turn),
    DiscardDownTo(p, cap: 10),   // excess cards go to Cache
    ClearCounter(p, pushes_this_turn)
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

// Turn-1 first-player special: skip Rise draw.
Game.abilities += Replacement(
  on:           Event.Draw(player=Game.first_player, amount=1,
                           triggered_by=phase_rise),
  guard:        Game.round == 1 ∧ is_first_turn_of_first_player,
  replace_with: NoOp)
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
Game.abilities += Static(
  modifies: BoardState,
  check_at: continuously,
  rule: ForEach(c -> c.kind == Conduit ∧ c.integrity <= 0 ∧ not collapsed_for(c.owner, c.arena),
          Sequence([
            SetFlag(Arena[c.arena].collapsed_for[c.owner], true),
            // All owner's Units and Standards attached to this Arena go to Cache.
            ForEach(e -> e.controller == c.owner
                       ∧ e.arena == c.arena
                       ∧ e.type ∈ {Unit, Standard-attached-to-Arena},
              MoveTo(e, c.owner.Cache))
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

### 4.5 Play-A-Card Protocol

This is the canonical sequence for playing any card from hand. The protocol is implemented as a Game-level procedure; each step is a distinct window that abilities can attach to.

```
Procedure PlayCard(p: Player, c: Card, targets: List, arena: Arena | None):
  // Step 1 — Announcement.
  EnterWindow(announcement, binding={player: p, card: c, targets: targets, arena: arena})
    // OnPlayed abilities fire here.
    // Interrupt opportunities open (see §4.5.1).

  // Step 2 — Cost computation.
  EnterWindow(cost_computation, binding=above)
    effective_cost := c.printed_cost
      applied_layer: cost_modifiers_for(c)     // Surge, Phantom, cost-reducers
      floor: 0

  // Step 3 — Pay cost.
  If (c has Interrupt) ∧ (Game.active_player != p):
    // Played on opponent's turn: debt instead of direct Aether.
    Require(p.debt + 2 * effective_cost <= p.aether_cap_schedule[Game.round])
            // can't overflow next turn to negative
    IncCounter(p, debt, 2 * effective_cost)
  Else:
    Require(p.aether >= effective_cost)
    PayAether(p, effective_cost)

  // Step 4 — Tier binding.
  EnterWindow(tier_binding, binding=above)
    tier_snapshot := evaluate all Tiers()/Resonance()/Banner() predicates
                     against p's current ResonanceField and state
    // Snapshot is frozen. Subsequent Interrupts cannot change tier outcomes.

  // Step 4.5 — Interrupt window (exhaustive).
  EnterWindow(post_tier_interrupt, binding=above)
    // Opponent (and p, for counter-Interrupts) may play Interrupts here
    // in strictly alternating fashion until both decline.

  // Step 5 — Resolution.
  EnterWindow(card_resolving, binding=above, tier_snapshot=tier_snapshot)
    case c.type:
      Unit     -> Instantiate(c, zone: Arena[arena].units[p], with states: {DeploymentSickness unless Blitz})
                   Apply OnEnter triggers.
      Maneuver -> Evaluate c.OnResolve(tier_snapshot, targets)
                   MoveTo(c, p.Cache)
      Standard -> Instantiate(c, attached_to: per-card)
                   Apply OnEnter triggers.

  // Step 6 — Push.
  If (c.pushes_echo = true) ∧ (c not a token, not a copy-effect play):
    PushEcho(c.factions)
    Append(p.pushes_this_turn, {card: c, factions: c.factions})
```

#### 4.5.1 Interrupts

Interrupts are Maneuvers with the Interrupt keyword. They are playable during Channel of either player, in response to a declared card or a pending trigger, before that thing resolves. The play protocol above shows where Interrupt windows open (`announcement`, `post_tier_interrupt`). An Interrupt played on the opponent's turn incurs `Debt = 2 × printed_cost` against next Rise's Aether refresh. Interrupts Push Echoes on the player who played them regardless of whose turn it is.

### 4.6 Clash Resolution

```
Procedure ResolveClash(active_player: p):
  EnterWindow(start_of_clash)
  DrainTriggersFor(window=start_of_clash)   // Phantom declarations happen here

  // Compute per-Arena outcomes simultaneously, then apply together.
  per_arena_results := {}
  ForEach arena ∈ {Left, Center, Right}:
    ForEach side ∈ {p, other_player(p)}:

      units_in_arena := Arena[arena].units[side], filtered for not-Phantoming

      projected_force[side] :=
        sum over u ∈ units_in_arena of
          case:
            u has Phantoming state     -> 0
            u has DeploymentSickness   -> 0
            u has Sentinel             -> 0
            default                    -> u.force + layer_2_modifiers(u)

      fortification[side] :=
        sum over u ∈ units_in_arena of
          case:
            u has Phantoming state -> 0
            u has Sentinel         -> u.force + u.current_ramparts
            default                -> u.current_ramparts

    incoming[p]          := max(0, projected_force[other_player(p)] - fortification[p])
    incoming[other(p)]   := max(0, projected_force[p] - fortification[other(p)])

    per_arena_results[arena] := { incoming_to: { p: incoming[p],
                                                  other: incoming[other(p)] } }

  // Apply all damage simultaneously.
  ForEach arena, side:
    If Conduit[side, arena] exists ∧ not collapsed_for(side, arena):
      DealDamageToConduit(Conduit[side, arena], per_arena_results[arena].incoming_to[side])

  EnterWindow(end_of_clash)
  DrainTriggersFor(window=end_of_clash)   // Phantom returns and cost-reductions happen here

  // Note: Units are NOT reduced in ramparts by Clash damage. Only Conduits take Clash damage.
  // Damage TO Units comes exclusively from Maneuvers, abilities, and Ignite.
```

### 4.6.1 A Note on Damage Events

Per GameRules §7.3 Clash damage bypasses Unit Ramparts entirely (it targets the Conduit directly, after Fortification absorbs). Damage from Maneuvers/abilities/Ignite targets Units explicitly and reduces their `current_ramparts`. Excess is lost; Ramparts restore at Fall (§7.3 last bullet) via the Rise sequence cleanup (actually, restate — the Fall phase restores before Pass; I model this as an end-of-Fall effect rather than Rise):

```
// Correction: Ramparts restore at end of controller's Fall phase, per §7.3.
Game.abilities += Triggered(
  on:       Event.PhaseEnd(phase=Fall, player=p),
  apply_at: before_pass,
  effect:   ForEach(u -> u.controller == p ∧ u.type == Unit ∧ u.zone ∈ battlefield,
              SetCounter(u, current_ramparts, max_ramparts_of(u))))
```

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
    OnCardPlayed(
      filter: c -> c.type == Maneuver ∧ EMBER ∈ c.factions,
      effect: When(Peak(EMBER),
                If(not has_been_copied_this_turn(event.card),
                   Sequence([
                     CopyManeuver(event.card),   // copy resolves, does not Push
                     Mark(event.card, copied_this_turn_by_eremis)
                   ]),
                   NoOp),
                check_at: announcement,
                apply_at: after_event))
  ]
  // text: Blitz. Peak EMBER: When you play an EMBER Maneuver, copy it. Each EMBER Maneuver
  //       can only be copied this way once per turn.
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
    //         from your Cache to your hand.
    OnCardPlayed(
      filter: c -> c.controller == self.controller ∧ TIDE ∈ c.factions,
      effect: When(Resonance(TIDE, 3),
        If(self.counters.thessa_used_this_turn == 0,
           Sequence([
             Choice(self.controller, {
               NoOp,
               Target(card -> card ∈ self.controller.Cache
                            ∧ TIDE ∈ card.factions
                            ∧ not (Unique ∈ card.keywords),
                 MoveTo(target, self.controller.Hand))
             }),
             IncCounter(self, thessa_used_this_turn, 1)
           ]),
           NoOp),
        check_at: announcement,
        apply_at: after_event))
    // thessa_used_this_turn is reset at Rise via the once_per_turn mechanism (§4.3 Rise).
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
    OnResolve(
      Target(m -> m.type == Maneuver
                ∧ m.state == pending_resolution
                ∧ m ∈ post_tier_interrupt_window,
        Sequence([
          CounterCard(target),    // Counter: removes from resolution, sends to caster's Cache without resolving
          // Override caster's Push to be HOLLOW instead of the card's printed faction(s).
          SetPushOverride(caster_of(target), factions: {HOLLOW}, for_this_play: target)
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

**Things that fell out of the primitives without adjustment.** Every keyword expressed as a macro. Every tier-table card as `Tiers(...)`. Every Banner-conditional as `When(Banner(F), ...)`. Every "counts echoes" card (Bloom, Myrrhan's Sapling buff) as `CountEcho(F)` or `Resonance(F, N)` directly, without a separate mechanism.

**Things that needed game-level procedural support.** Three: the play protocol (§4.5) with its explicit windows (`announcement`, `cost_computation`, `tier_binding`, `post_tier_interrupt`, `card_resolving`); the Clash resolution procedure (§4.6) which reads Phantoming/Sentinel/DeploymentSickness state when computing Force and Fortification; and the Collapse SBA (§4.4) which sweeps Units and Standards when a Conduit integrity hits 0. None of these are card-level concerns — they belong in the Game entity's rules.

**Things worth re-examining for authoring clarity.**

1. **Sprawl targeting.** GameRules §11 says Sprawl creates tokens "in this Arena," which makes sense for a Unit with Sprawl but is undefined for a Maneuver (Take Root) — a Maneuver has no arena. I encoded Take Root as `Target(arena, …)`, inferring author intent. The game rules should state this explicitly, or Sprawl should be split into `Sprawl-Here` (Unit) and `Sprawl-Chosen` (Maneuver).

2. **"When you play a card" timing.** Surge, OnCardPlayed, The Standing Wave, Thessa, Eremis. All check at `announcement` and fire before the triggering card resolves. Several of these interact subtly with tier binding — e.g., The Standing Wave's Reshape fires before the triggering card's tier is bound, which means a Reshape can retroactively flip a tier. Worth a playtest note.

3. **Eremis "copy" does not Push.** GameRules §6.4 establishes this; I encoded it by having `CopyManeuver` bypass the Push step. Worth making this a first-class distinction on the play protocol: a `played_from_hand` flag on the play binding.

4. **The Unread Page Peak interaction.** Current encoding: opponent's choice is locked at Enter-Play, then if Peak is reached later, both modifiers are forced on. If Peak is reached then lost then reached again, the encoding re-applies — the choice is never reverted. That matches my reading of the card but it's worth confirming.

5. **Thessa's once-per-turn flag.** Encoded as a counter on the Unit, reset at Rise. This pattern (`once_per_turn` activation tracking) should probably be a first-class macro rather than hand-rolled.

6. **Harborkeeper's "would enter" replacement.** Requires the play protocol to emit `Event.EnterPlay` as a *pending* event that Replacement abilities can intercept with an alternate arena. This is standard MTG-style would-event plumbing but it's worth being explicit that your Events model distinguishes `Event.X_pending` from `Event.X_committed`.

**Open schema questions for this game specifically.**

- Is `PhantomReturn` a distinct Event type, or a subtype of `MoveTo(target → Hand)`? I treated it as distinct to let Blankface and Oreth trigger cleanly; if you want to collapse it, Phantom's `ScheduleAt(end_of_clash, MoveTo(self, Hand))` needs to carry a `reason: phantom_return` tag and triggers filter on that tag.

- Should `DeploymentSickness` be universal (implicit on every Unit) or explicit in the keyword list? I listed it explicitly per-card. Implicit would be cleaner; it's a default-on rule. Recommend: move it to a Game-level Triggered ability that fires on every `Event.EnterPlay(target.type == Unit)` and relies on Blitz's replacement rule to suppress.

- The Aether refresh schedule is hardcoded on Player. Consider making it a Game characteristic so a future "fast mode" format can override it without touching Player entities.

---

*End of encoding. If any card or rule needs expansion, re-derivation from primitives, or cross-translation back to the other game's encoding, the macro library is the right place to start.*
