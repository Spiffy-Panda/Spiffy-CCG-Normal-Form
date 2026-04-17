# RESONANCE — Supplement

Companion to `GameRules.md`. This file is mostly data: example cards, faction identity detail, keyword specifics, and the draft environment. Rules live next door; this is the showroom.

---

## 1. Faction Identities

The five factions are not just color-coded archetypes; each has a distinct relationship with the Resonance Field and with the three Arenas. When designing new cards, the question is always: *"Does this want to be mono? Does this want to be spread across Arenas? What does it make you afraid of?"*

### 1.1 EMBER — The Oncoming Wave

- **Plays to win by turn 6–7.** Every card either advances the Conduit clock or protects your tempo.
- **Relationship to Resonance**: loves being early to Peak. Many EMBER cards trigger Surge on second-or-later EMBER plays in a turn, rewarding a quick burn-down.
- **Relationship to Arenas**: usually picks one Arena and pours everything in. Ember is brittle if forced to split.
- **Weaknesses**: runs out of cards. Struggles through persistent Fortification. Hates Phantom (attacks that "don't land").
- **Fear**: a BULWARK Sentinel wall + a topdecked Mend.

### 1.2 BULWARK — The Long Song

- **Plays to survive to turn 10+ and win from a stabilized position.**
- **Relationship to Resonance**: reaches Peak late, and many BULWARK Peak effects are explicitly win-condition-grade (ramparts that project Force, Conduit re-constructions).
- **Relationship to Arenas**: holds all three. A BULWARK deck that cedes an Arena is often losing.
- **Weaknesses**: can't close on its own without Peak; vulnerable to HOLLOW resource attack.
- **Fear**: an EMBER deck that reaches Peak on turn 4.

### 1.3 TIDE — The Standing Wave

- **Plays to rearrange the board until one Arena is a disaster for the opponent.**
- **Relationship to Resonance**: the only faction with real tools to *reshape* its Field. Reshape lets TIDE stay on-tier even while splashing. This is expensive and deliberate.
- **Relationship to Arenas**: the only faction that routinely moves Units between Arenas via Drift.
- **Weaknesses**: low raw Force. Easily overrun by THORN swarms or EMBER rush.
- **Fear**: a wide THORN board that can't be bounced fast enough.

### 1.4 THORN — The Many

- **Plays to have the widest board, always.**
- **Relationship to Resonance**: THORN cards often *count Echoes* rather than just gating on them. "For each THORN in your Field, +1/+0." Rewards commitment continuously, not just at breakpoints.
- **Relationship to Arenas**: spreads Units, but commits hard to one Arena as the "tall" Arena.
- **Weaknesses**: vulnerable to sweepers; slow to turn the corner without Kindle payoffs.
- **Fear**: a BULWARK board wipe, or a TIDE player that bounces their key Kindle Standard.

### 1.5 HOLLOW — The Missing Page

- **Plays to make the opponent's deck do less.**
- **Relationship to Resonance**: can *disrupt* the opponent's Field via Pilfer (which can exile from hand before the opponent plays the card, pre-empting an Echo). Cannot directly manipulate its own Field.
- **Relationship to Arenas**: doesn't care. HOLLOW Units Phantom between Arenas; attachment to territory is weakness.
- **Weaknesses**: low direct damage, struggles to close.
- **Fear**: a BULWARK deck with Mend + Sentinel; a THORN deck with a full board the turn before Pilfer lands.

### 1.6 NEUTRAL

Neutral cards are glue. They should never be the best card in a pure deck of any faction, but they should be the best card in a *splashed* deck often enough that splashing feels rewarding. Design target: a Neutral common is roughly 85% as strong as a faction common of equivalent cost; it earns back the last 15% by not Pushing off your Banner.

---

## 2. Keyword Details

Expanded from the core list in `GameRules.md §11`. Includes timing and edge cases.

### 2.1 EMBER Keywords

- **Surge**: Checks at the moment you begin to play the card. "Another EMBER Echo Pushed this turn" means an EMBER Echo that was Pushed by a card you played earlier this same turn (not across turns). Dual-faction cards containing EMBER count.
- **Blitz**: A unit with Blitz bypasses **Deployment Sickness** (see §2.6). Blitz does not grant ongoing benefits; it is purely a first-turn effect.
- **Ignite X**: At the start of your turn, deal X damage to every opposing Unit in this Arena with current Ramparts 2 or less. Ignite fires in the "start of your turn" window — after your Rise draw, before Channel.

### 2.2 BULWARK Keywords

- **Fortify X**: Conditional on the Conduit's Integrity. If the Conduit drops below 4, Fortify suppresses; if it heals back up, Fortify returns. This creates a BULWARK tension: a damaged Conduit causes its own defenders to weaken.
- **Mend X**: Healing cannot exceed the Conduit's starting Integrity (7). Mend on a Collapsed Arena fizzles.
- **Sentinel**: A Sentinel Unit contributes **zero** Force to Projected Force, but its Force value is added to the defender's effective Fortification. Sentinel Units are thus "pure walls." Sentinel does not stack with itself in a silly way — each Sentinel adds its own Force.

### 2.3 TIDE Keywords

- **Drift**: An optional move resolved during the "End of your turn" window (i.e., during Fall, before Pass). Drift is per-Unit and per-turn — you decide for each Drift Unit independently. A Drifting Unit cannot enter an Arena that is Collapsed for its controller. Note that your own Units are already in your Cache if your Conduit in their Arena is destroyed (see GameRules §7.4), so the source of a Drift is always a non-Collapsed Arena for that Unit's controller.
- **Recur**: Replaces the send-to-Cache step. If Recur is *prevented* (e.g., by exile), Recur does not fire.
- **Reshape**: Only affects your own Field. Never the opponent's. Each Reshape moves one Echo one slot. Cards with Reshape typically also do something else.

### 2.4 THORN Keywords

- **Rally**: Triggers only for friendly Units, and only when they *enter* your Arena (from hand, via Sprawl, or via Drift-in). It does not trigger on a Unit already in the Arena.
- **Sprawl X**: Saplings enter with **Deployment Sickness** and gain Force next turn, like normal Units. Saplings are NEUTRAL; they do not Push Echoes (they were not played from hand).
- **Kindle**: Kindle counters accumulate until 3, then the Standard's effect fires and resets. If the Standard gains Kindle counters faster than once per turn (via effects), it may fire multiple times in a single turn. Kindle is exclusively on Standards in the base set.

### 2.5 HOLLOW Keywords

- **Phantom**: Declared at the **Start of Clash** window, per-Arena. A Phantoming Unit contributes 0 Force *and* 0 Fortification for that Clash, then returns to its owner's hand at End of Clash with a permanent −1 Aether cost counter (minimum 0). Cost counters persist across hand, Arsenal, and Cache. A Unit that re-enters play from hand gains Deployment Sickness again. Phantom is a strict tempo trade: you dodge a Clash but get no defensive value that turn.
- **Shroud**: Blocks only *targeted* opposing effects. Sweepers that affect all Units (or all Units in an Arena) are not blocked. Your own targeted effects pass through Shroud freely.
- **Pilfer X**: The opponent reveals their hand; you choose **up to** X cards to be exiled to the bottom of their Arsenal. If the opponent has fewer than X cards in hand, you may exile any subset of what is available. Exile, not Cache — these cards cannot be Recur'd or returned to hand. Pilfer is a narrow, brutal tool.

### 2.6 Universal — Deployment Sickness

Canonical definition lives in GameRules §10.1. A quick restatement: a Unit deployed this turn projects 0 Force at Clash but contributes Ramparts normally (so it can fortify on its deployment turn). Deployment Sickness ends at the start of its controller's next Rise. **Blitz** overrides it. A small number of non-EMBER cards may grant "this enters without Deployment Sickness" as a premium effect, but Blitz is otherwise EMBER's signature.

Design note: this is a major tempo lever. Blitz turns a Unit's first turn from defensive-only into offensive-and-defensive, which is why Blitz is disproportionately placed on small EMBER creatures. Bigger Blitz Units would compress the game clock too hard.

---

## 3. Example Cards

Notation: `Name (Faction, Type) — Cost — F/R` (for Units) or `Name (Faction, Type) — Cost` (for Maneuvers/Standards). Rarity shown in brackets: [C], [U], [R], [M] (mythic, effectively ultra-rare).

### 3.1 EMBER

**Commons**

- **Cinderling** [C] *(EMBER, Unit) — 1 — 2/1*
  Blitz.
  *A 1-drop that pressures the Conduit immediately. The backbone EMBER one-drop.*

- **Spark** [C] *(EMBER, Maneuver) — 1*
  Deal 2 damage to a Unit or Conduit.
  **EMBER 3:** Deal 3 instead.

- **Emberhand Raider** [C] *(EMBER, Unit) — 2 — 3/2*
  Surge.
  *Costs 1 if another EMBER Echo was Pushed this turn. The engine card of curve-out EMBER.*

- **Smolder** [C] *(EMBER, Maneuver) — 2*
  Deal 2 damage to a target Unit or Conduit.
  **EMBER 4:** Deal 4 instead.

**Uncommons**

- **Pyrebrand** [U] *(EMBER, Unit) — 3 — 4/2*
  Blitz. Ignite 1.
  **EMBER 3:** Ignite 2 while this is in play.

- **Stokefire** [U] *(EMBER, Maneuver) — 3*
  Deal 3 damage, split among any number of targets.
  **Peak EMBER:** Deal 5 damage, split as you choose.

**Rare**

- **Choir of the Blaze** [R] *(EMBER, Standard — attaches to an Arena) — 4*
  At the start of your turn, deal 1 damage to each opposing Unit in this Arena.
  **EMBER 4:** Also deal 1 damage to the opposing Conduit in this Arena.

**Mythic**

- **Eremis, the Unfaded** [M] *(EMBER, Unit — Unique) — 5 — 5/3*
  Blitz.
  **Peak EMBER:** When you play an EMBER Maneuver, copy it. Each EMBER Maneuver can only be copied this way once per turn.
  *The Peak payoff that rewards having gone fully mono EMBER. 30% win rate alone; 70% at Peak.*

### 3.2 BULWARK

**Commons**

- **Palisade** [C] *(BULWARK, Unit) — 1 — 0/3*
  Sentinel.
  *Pure wall. Adds 0 Force to Projected Force; 3 to Fortification.*

- **Warden Initiate** [C] *(BULWARK, Unit) — 2 — 2/3*
  Fortify 1.
  *Grows in defended Arenas; shrinks when you're losing.*

- **Seal the Breach** [C] *(BULWARK, Maneuver) — 2*
  Heal target Conduit 3. **BULWARK 3:** Heal 5 instead.

- **Hold the Line** [C] *(BULWARK, Maneuver — Interrupt) — 2*
  Prevent up to 4 Incoming in one Arena this turn.
  *Cost 2 → 4 Debt when played on opponent turn. Designed as a mid-to-late-game stabilizer, not a constant-pressure counter.*

**Uncommons**

- **Cohort Captain** [U] *(BULWARK, Unit) — 3 — 3/4*
  When another BULWARK Unit enters this Arena, it gains Fortify 1 while Cohort Captain is in play.

- **Reconstitute** [U] *(BULWARK, Maneuver) — 4*
  Heal target friendly Conduit to its starting Integrity. If that Conduit is destroyed (your side of that Arena is Collapsed), instead restore the Conduit at 3 Integrity and un-Collapse your side of that Arena. You may once again deploy Units and Arena-Standards there. Units lost when the Conduit was destroyed are not returned.
  *The only card in the base set that un-Collapses. Rare and slow by design.*

**Rare**

- **The Quiet Wall** [R] *(BULWARK, Standard — attaches to player) — 5*
  All your Units have Sentinel in addition to their other properties. Your Units' Force is still counted for effects that reference Force; it just does not Project at Clash.
  **Peak BULWARK:** Your Units Project 1 Force each at Clash despite Sentinel.

**Mythic**

- **Vaen, Architect of the Last Hour** [M] *(BULWARK, Unit — Unique) — 6 — 2/7*
  Mend 3. Sentinel.
  **BULWARK 4:** While this tier is satisfied, each friendly Unit in Vaen's Arena has +2 Ramparts.
  **Peak BULWARK:** Your Conduits cannot be reduced below 1 Integrity by opposing effects. This property is lost if Vaen leaves play.
  *Tier 4 buff is ongoing-conditional, not a turn-start trigger — it applies to both players' Clashes as long as you stay at BULWARK 4+.*

### 3.3 TIDE

**Commons**

- **Ripplekin** [C] *(TIDE, Unit) — 1 — 1/2*
  Drift.
  *The little Unit that can't be pinned down.*

- **Refract** [C] *(TIDE, Maneuver) — 1*
  Return target Unit (yours or opponent's) to its owner's hand.

- **Brinescribe** [C] *(TIDE, Unit) — 3 — 2/3*
  When you play this, draw a card. Drift.

- **Reshape the Current** [C] *(TIDE, Maneuver) — 2*
  Reshape twice.

**Uncommons**

- **Harborkeeper** [U] *(TIDE, Unit) — 3 — 3/3*
  When another of your Units would enter a Collapsed Arena of yours, it may enter an adjacent Arena of yours instead (if non-Collapsed).

- **Undertow** [U] *(TIDE, Maneuver) — 3*
  Move each Unit in target Arena one Arena to the left or right (you choose per Unit). Units that would leave the edge stay.
  **TIDE 4:** Instead, choose any reassignment.

**Rare**

- **The Standing Wave** [R] *(TIDE, Standard — attaches to player) — 4*
  When you play a card, you may Reshape once.
  **Peak TIDE:** When you play a card, you may also move one Unit you control to a new Arena.

**Mythic**

- **Thessa, Who Returns** [M] *(TIDE, Unit — Unique) — 5 — 3/4*
  Drift. Recur.
  **TIDE 3:** When you play a TIDE card, you may return a non-Unique TIDE card from your Cache to your hand. Once per turn.
  *A self-refueling engine that can push Peak TIDE onto the table reliably.*

### 3.4 THORN

**Commons**

- **Thornling** [C] *(THORN, Unit) — 1 — 1/1*
  Rally.
  *One-drop that scales if the floor keeps filling.*

- **Take Root** [C] *(THORN, Maneuver) — 2*
  Sprawl 2.

- **Grove-Kin Druid** [C] *(THORN, Unit) — 2 — 2/2*
  When you play this, friendly Units in this Arena gain +1 Ramparts until end of turn.

- **Kindling Brazier** [C] *(THORN, Standard — attaches to Arena) — 2*
  Kindle: Sprawl 1.

**Uncommons**

- **Thicketwarden** [U] *(THORN, Unit) — 3 — 3/4*
  While there are 3+ friendly Units in this Arena, this Unit has +2 Force.

- **Bloom** [U] *(THORN, Maneuver) — 3*
  For each THORN Echo in your Resonance Field, create a 1/1 Sapling in an Arena of your choice.
  *Counts Echoes, does not gate on a tier.*

**Rare**

- **The Endless Thicket** [R] *(THORN, Standard — attaches to player) — 5*
  At the start of your turn, Sprawl 1 in each of your Arenas that contains 2+ friendly Units.
  **Peak THORN:** Sprawl in every Arena regardless of Unit count, and Saplings enter with +1/+0.

**Mythic**

- **Myrrhan, Keeper of Growth** [M] *(THORN, Unit — Unique) — 5 — 4/5*
  Rally.
  **THORN 3:** Your Saplings are 2/2 instead of 1/1.
  **Peak THORN:** At the start of your turn, Sprawl 2 in each Arena you have a Unit in.

### 3.5 HOLLOW

**Commons**

- **Veilslip** [C] *(HOLLOW, Unit) — 1 — 2/1*
  Phantom.

- **Private Thought** [C] *(HOLLOW, Maneuver) — 1*
  Opponent reveals two cards from their hand; you exile one to the bottom of their Arsenal.

- **Greyface** [C] *(HOLLOW, Unit) — 2 — 2/2*
  Shroud.

- **Unmake** [C] *(HOLLOW, Maneuver) — 3*
  Destroy target Unit with Force 3 or less.
  **HOLLOW 3:** Force 4 or less instead.

**Uncommons**

- **The Last Whisper** [U] *(HOLLOW, Maneuver — Interrupt) — 3*
  Counter target Maneuver. Its caster Pushes a HOLLOW Echo instead of its printed faction.
  *Cost 3 → 6 Debt on opponent turn. A late-game hard counter that also attacks the opponent's Resonance Field. Under the steep Debt rule, this is the HOLLOW endgame play, not a casual counter.*

- **Blankface Cultist** [U] *(HOLLOW, Unit) — 3 — 3/3*
  Phantom. When this returns to your hand via Phantom, Pilfer 1.

**Rare**

- **The Unread Page** [R] *(HOLLOW, Standard — attaches to opponent) — 4*
  *Your opponent chooses: their maximum hand size is 5, or their starting Aether each turn is reduced by 1, while this is in play.*
  **Peak HOLLOW:** They do not get to choose; both apply.

**Mythic**

- **Oreth, the Unseen Author** [M] *(HOLLOW, Unit — Unique) — 6 — 4/3*
  Shroud. Phantom.
  **HOLLOW 4:** When Oreth enters play, Pilfer 2.
  **Peak HOLLOW:** When Oreth returns to your hand via Phantom, the opponent exiles a card from their hand at random.

### 3.6 Dual-Faction (Sample)

- **Ashroot Whelp** [U] *(EMBER/THORN, Unit) — 2 — 2/2*
  Blitz. Rally.
  *Rally packaged with Blitz; Pushes both EMBER and THORN. The on-curve 2-drop for the EMBER/THORN Go-Wide Blitz archetype.*

- **Tidecaller's Hollow** [R] *(TIDE/HOLLOW, Maneuver) — 3*
  Return target Unit to its owner's hand, then that player exiles a card from their hand at random.

### 3.7 Neutral

- **Baseline** [C] *(NEUTRAL, Unit) — 1 — 1/1*
  *The provided draft filler. Does not Push any Echo (tournament rule specific to Baseline; does not generalize to Neutral cards).*

- **Conduit Tender** [C] *(NEUTRAL, Unit) — 2 — 1/3*
  Mend 1.
  *Weak. Useful as a splash in any deck but never the best card. Canonical "Neutral glue."*

- **Scavenged Blade** [C] *(NEUTRAL, Maneuver) — 2*
  Deal 2 damage to a Unit. **If your Banner exists:** Deal 3 instead.
  *Scales with whatever you're doing.*

- **Wayfarer** [U] *(NEUTRAL, Unit) — 3 — 3/3*
  When you play this, if your Banner exists, draw a card.

---

## 4. Rarity Guidelines

- **Common [C]**: The medium of the game. Simple, legible, often one keyword. Every common should be playable in its faction's main deck.
- **Uncommon [U]**: More complex. Often two abilities or one conditional ability. Introduces design-space specific to the faction.
- **Rare [R]**: Powerful and directional. Rares define archetypes.
- **Mythic [M]**: Cards that are more about creating games than winning them. Often Unique (limit 1). Designed to have their Peak text be the memorable moment of a match.

Distribution targets per 250-card set:

| Rarity | Count | % |
|---|---|---|
| Common | 110 | 44% |
| Uncommon | 80 | 32% |
| Rare | 45 | 18% |
| Mythic | 15 | 6% |

---

## 5. Resonance Tier Design Notes

A few internal rules of thumb for card design:

- **Tier 2 effects** should be small, almost-always-live bonuses. They are the baseline for splash decks.
- **Tier 3 effects** should be "obviously good, not game-ending." This is what a loose mono-faction deck hits routinely.
- **Tier 4 effects** should feel like tempo spikes — "now I'm ahead" moments.
- **Peak effects** should be memorable. A Peak effect that doesn't make you smile should be re-designed.
- **Cards should rarely have all four tiers**. Most cards have 1–2 tiered effects. Stacking four tiers is a mythic-level design pattern.
- **The base effect must be standalone playable.** A card that is dead without Peak is a draft trap; no common should require Peak to function.

Rough power-budget formula (for designer gut-check, not for player-facing display):

```
Card Power ≈ BaseEffectValue
           + 0.3 × Tier2Value
           + 0.5 × Tier3Value
           + 0.8 × Tier4Value
           + 1.2 × PeakValue
```

The multipliers reflect how often a deck is actually *at* that tier. A well-built mono deck hits Tier 4 about half the time and Peak about 25% of the time by mid-game. A 2-faction split deck hits Tier 3 of its main faction often, Tier 4 rarely, and Peak almost never.

---

## 6. Draft Environment Design

### 6.1 Faction Density

A draftable set should have, per 15-card pack:

- ~7 Neutral cards (filler, utility)
- ~1.6 cards of each faction on average
- ~1 dual-faction card

A player seeing 1.6 cards of their faction per pack averages 24 cards of that faction over a full draft. They will end with ~18 in their 30-card deck after filtering. This supports Tier 3 play consistently and Tier 4 play occasionally.

### 6.2 Signal Reading

Because the Resonance Field is a rolling window, a mono-focused draft deck wins Peak more often than a split deck — but a split deck can set up Peak on a specific critical turn through deliberate sequencing. The set should include a handful of "setup" cards (low-cost Maneuvers that Push cheaply) in each faction to support this.

### 6.3 Archetype Coverage in a Launch Set

Target 10 archetypes, each roughly equally supported:

1. EMBER Mono Rush
2. BULWARK Mono Wall
3. TIDE Mono Control
4. THORN Mono Swarm
5. HOLLOW Mono Disruption
6. EMBER/THORN Go-Wide Blitz
7. BULWARK/TIDE Stabilize-and-Transform
8. HOLLOW/TIDE Tempo Denial
9. THORN/BULWARK Big Mid-range
10. EMBER/HOLLOW Sacrifice-and-Pressure

Each archetype should have at least two uncommons or rares that explicitly reward it.

---

## 7. Design Space Still to Explore

Reserved for future iteration — not in the base set, but tracked so the game has somewhere to grow:

- **Arena-specific resonance**: a faction's Resonance counted only for that Arena. Would require a separate per-Arena Field.
- **Shared Resonance effects**: "If your Banner and opponent's Banner match, …" as a dueling-mirror mechanic.
- **Echo manipulation as a full subtheme** (currently only TIDE touches it; HOLLOW only attacks the *opponent's* Field indirectly via Pilfer).
- **Conduit unique powers**: replacing the generic 7-Integrity Conduit with faction-flavored Conduits in a separate format.
- **Asymmetric Arenas**: each Arena has a persistent property (e.g., "Left: Units here have +1 Force. Center: no Maneuvers may be played. Right: Standards cost 1 less here.") that changes per match or per round.

Each of these is a lever that could be the centerpiece of a future expansion. None should be added without evaluating what it breaks.

---

## 8. Open Questions (designer self-notes)

### Still open

- Should there be a maximum number of Neutral Echoes? Currently no. A stall of Neutrals could strip your Banner entirely — this might be a feature (punishment for going too generic) or a bug (unintentional soft-lock of your identity). Playtest.
- Should Conduits regenerate Integrity naturally over time, or only via Mend? Current rules: only Mend. May feel punishing against EMBER. Consider: "At the start of your turn, each of your Conduits regenerates 1 Integrity (max: starting Integrity)" for non-Collapsed Conduits.
- Is 40-card constructed / 30-card draft the right ratio? Some internal arguments favor 50/35 for more variance. Playtest.
- How does the 2× Debt rule interact with Interrupt card *printed* costs? Are 1-cost Interrupts worth printing at all (2 Debt = manageable)? Are 4+ cost Interrupts effectively never played off-turn? First pass: 1-cost Interrupts are probably correct for minor reactive effects (bounce, small damage prevention); 4+ cost Interrupts are probably not worth printing unless they win the game.
- Do we need a "cannot be Interrupted" keyword for key threats, given how steep Debt is? Probably not at launch, but track if Interrupts feel too dominant.

### Resolved (moved from earlier drafts)

- ~~How should "copy a card" effects interact with Resonance?~~ **Resolved**: copies do not Push Echoes (GameRules §6.4). Only plays from hand Push.
- ~~Should Interrupts exist at all?~~ **Resolved**: yes, with 2× Debt (GameRules §6.3). Steep enough to be the rare reactive tool, cheap enough to exist as a design axis.
- ~~Second-player compensation: extra card, extra Aether, or both?~~ **Resolved**: extra card only. Simpler and tested to be sufficient in curve-driven designs.

---

## 9. Card Format Reference

For any future card-data work, this is the canonical field list for a single card. This is a *data model* for cards, not an implementation choice — it is the set of things a card's text is responsible for carrying.

- `id` (stable string)
- `name`
- `factions` (array of 1 or 2 values from {EMBER, BULWARK, TIDE, THORN, HOLLOW, NEUTRAL}. A NEUTRAL card is `["NEUTRAL"]`. Dual-faction cards are e.g., `["EMBER", "THORN"]`. A card is never factionless *and* non-neutral.)
- `type` (Unit | Maneuver | Standard)
- `subtype` (e.g., Standard attaches to Arena / Player / Opponent)
- `cost` (integer, ≥0)
- `force` / `ramparts` (integers, Units only)
- `keywords` (array of keyword strings; includes Interrupt if applicable)
- `text` (authored rules text, human-readable)
- `tier_effects` (structured: faction → threshold → effect; `threshold = 5` is Peak)
- `banner_effects` (structured: faction-or-"any" → effect, for Banner-conditional text)
- `rarity` (C | U | R | M)
- `unique` (bool; Uniques are limited to 1 per Arsenal in Constructed)
- `set` (string)
- `collector_number` (int)
- `flavor` (authored, separate from rules)
- `pushes_echo` (bool, defaults true; false only for special cards like draft Baseline)

Rules text should be authored as a tight, unambiguous sentence structure. Natural-language "and" / "or" are fine; stack-based phrasing ("if X then Y unless Z") should be avoided in common cards.

---

*End of Supplement. Iterate here before iterating in the rules document — this is where the game grows.*
