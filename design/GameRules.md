# RESONANCE — Game Rules

*A two-player collectible card game of channels, arenas, and standing waves.*

Working title. Terminology may shift; mechanics are committed.

---

## 1. Vision

Two players conduct rival currents across three battlefields. Each card you play leaves an echo; five echoes form your **Resonance Field**, and the more a single faction dominates that field, the more powerfully your cards sing. There is no declared faction, no land search, no tap to attack, and no blocking. The game rewards commitment without punishing flexibility — you can splash, but you will rarely reach peak resonance while you do.

The central design tension: every card you play is both *an action now* and *a vote for who you are*. Your last five plays are the entirety of your identity on the table.

---

## 2. Core Concepts (at a glance)

| Term | Meaning |
|---|---|
| **Arena** | One of three parallel battlefields: Left, Center, Right. |
| **Conduit** | A player's anchor in an Arena. Each player has 3, one per Arena, each with 7 Integrity. |
| **Unit** | A card deployed to a specific Arena. Has Force and Ramparts. |
| **Maneuver** | A one-shot card. Resolves, then goes to the Cache. |
| **Standard** | A persistent card attached to an Arena or the player. |
| **Aether** | The primary resource, refreshed each turn. |
| **Hand** | Private set of cards available to play. Capped at 10. |
| **Resonance** | A rolling window of your last 5 card plays, scored per faction. |
| **Banner** | The faction with the highest count in your Resonance Field. |
| **Clash** | The combat phase in each Arena, resolved on the active player's turn. |
| **Arsenal** | Your deck. |
| **Cache** | Your discard pile. |
| **Echo** | A single entry in the Resonance Field. |
| **Permanent** | A Unit or Standard currently in play. (Conduits are not Permanents.) |

---

## 3. Components

### 3.1 Card Anatomy

Every card has:

- **Name**
- **Aether cost** (a non-negative integer)
- **Faction(s)** — one of EMBER, BULWARK, TIDE, THORN, HOLLOW, or NEUTRAL. A small number of cards are dual-faction (see §9.7).
- **Type** — Unit, Maneuver, or Standard.
- **Stats** (Units only): **Force** / **Ramparts**, written `F/R`.
- **Rules text**, which may include:
  - **Keywords** (e.g., Surge, Drift, Phantom)
  - **On-Play** effects
  - **Ongoing** effects
  - **Resonance tiers** — see §8.

### 3.2 Zones

- **Arsenal** — face-down deck, ordered, private.
- **Hand** — private, cap 10. Cards beyond the cap at end of turn are sent to the Cache.
- **Arenas** — three public zones, each holding Units, Standards, and each player's Conduit.
- **Cache** — face-up, public, ordered.
- **Resonance Field** — a public row of up to 5 Echo slots for each player; see §8.
- **Banner** — a derived public marker; see §8.3.

---

## 4. Setup

1. Each player brings a legal Arsenal (see §13).
2. Randomly determine the First player.
3. Each player places three Conduits, one in each Arena, each at 7 Integrity.
4. Shuffle Arsenals. First player draws 5 cards. **Second player draws 6 cards** — this is the sole compensation for going second.
5. **Tuning (mulligan)**: Each player may, up to twice, perform a mulligan. To mulligan: choose at least one card from your hand, place those cards on the bottom of your Arsenal in any order (no shuffling), then draw one fewer card than you placed. A mulligan that would draw zero or fewer cards is still legal (you simply remove cards from the game's useful pool). You may not mulligan by returning zero cards.
6. The Resonance Field is empty for both players.
7. First player begins Round 1.

**Note on bottom-stacking:** Mulliganed cards are placed on the bottom of the Arsenal in a deterministic order of the player's choice. This is deliberate — it makes each mulligan a real resource trade (card quantity and card quality in exchange for specific draws) rather than a free reshuffle.

---

## 5. Turn Structure

A round consists of the First player's turn, then the Second player's turn. A turn has five phases, in order:

1. **Rise** —
   - Refresh your Aether to this round's total (see §6.1).
   - Clear all damage from your Units (Ramparts return to their current maximum).
   - Reset any "once per turn" abilities you control.
   - Draw 1 card. **If your Arsenal is empty and you must draw, you lose the game.**
   - Trigger all "start of your turn" effects.
2. **Channel** — The active player may play cards and activate abilities. Any number of each, in any order, paying Aether and other costs. There is no reactive priority inside this phase — once a card or ability resolves, the next action begins. (See §6.3 for the one exception: **Interrupts**.)
3. **Clash** — All three Arenas resolve combat simultaneously. See §7.
4. **Fall** — Trigger "end of your turn" effects. Discard to 10.
5. **Pass** — Turn ends; opponent's Rise begins.

Round count advances after both players have completed a turn.

### 5.1 Timing Windows

For cards and abilities with timed triggers, the named windows are:

- **Start of your turn** — during Rise, after your Aether refresh but before your draw. (Draw itself is not a trigger window; effects that "trigger on drawing" are explicit.)
- **When you play [card/type]** — the instant a card is announced, before its cost is paid.
- **When [a card] enters play** — after the card has finished resolving and is in its zone.
- **Start of Clash** — the instant Clash begins, before Force and Fortification are computed.
- **End of Clash** — after all three Arena resolutions complete.
- **End of your turn** — during Fall, before Pass.

When multiple triggers occur in the same window, the active player orders their own triggers first, then the opponent orders theirs.

---

## 6. Playing Cards

### 6.1 Aether

Aether is **refreshed, not accumulated**. Unspent Aether at end of turn is lost. The scaling schedule is identical for both players:

| Round | Aether |
|---|---|
| 1 | 3 |
| 2 | 4 |
| 3 | 5 |
| 4 | 6 |
| 5 | 7 |
| 6 | 8 |
| 7 | 9 |
| 8+ | 10 (cap) |

This fixed curve removes mana screw/flood as a variance axis. Variance in this game comes from draw order and Resonance alignment, not resource production.

### 6.2 Playing a Card

To play a card from your hand:

1. Announce the card and, for Units, the target Arena.
2. Compute the effective Aether cost: printed cost, adjusted by any cost modifiers (e.g., **Surge**, Phantom discount). Cost cannot go below 0.
3. Pay the cost in Aether (and any other printed costs).
4. Determine which Resonance tier applies (if any), using your Resonance Field **as it currently exists** — the card being played does not yet count.
5. Resolve the card:
   - Units enter the chosen Arena. They suffer **Deployment Sickness** (see §10.1) unless Blitz or an equivalent effect overrides.
   - Maneuvers resolve their effect and go to the Cache.
   - Standards attach and enter play.
6. **Push** the card's faction into your Resonance Field (see §8.2). Push happens *after* the card resolves.

### 6.3 Interrupts

A small number of Maneuvers have the **Interrupt** keyword. Interrupts may be played during either player's turn, during their Channel phase, in response to a declared card or a trigger, before that card or trigger resolves. Only Interrupts can be played reactively; there is no general priority system. This is deliberate: we want a clean, low-overhead action economy, with sharp reactive tools as exceptions rather than the default.

An Interrupt played on your own turn is paid for in Aether normally. An Interrupt played on the **opponent's** turn incurs **Debt** equal to **twice its printed cost**, deducted from your next Rise's Aether refresh. The doubling is deliberate: a reactive tool should cost more than a proactive one of the same impact, and burning most of next turn to cancel this turn's threat should feel like a real sacrifice.

You may not take on Debt that would leave your next turn's Aether below 0. (Equivalently: your total Debt cannot exceed the Aether you would refresh to at your next Rise, *before* Debt is applied.) In practice: a 2-cost Interrupt (4 Debt) is playable on opponent turns from Round 2 onward and costs most of your next turn; a 3-cost Interrupt (6 Debt) is playable from Round 4 onward and leaves you with 0–3 Aether to follow up. Interrupts are never cheap tempo — they are a choice to skip most of a turn.

Interrupts played from hand Push an Echo on the playing player's Resonance Field at the normal time (§6.2 step 6), regardless of whose turn it is.

### 6.4 Copies, Tokens, and Echo Push

Only cards **played from hand** Push Echoes onto your Resonance Field. Tokens created by effects (e.g., Thorn Saplings from Sprawl) do not Push. Cards played by "copy" effects (e.g., a Maneuver that copies another Maneuver) do not Push — the original card's play already Pushed its Echo.

Effects that return a card from the Cache or Arsenal to your hand do not themselves Push; only the subsequent play of that card from hand will Push.

---

## 7. Clash (Combat)

Clash resolves all three Arenas simultaneously. Mechanically, each Arena is computed independently and the results are applied together, so Arena resolution order is never player-visible. Triggers that depend on Clash outcomes all occur in the "End of Clash" timing window (see §5.1).

### 7.1 Per-Arena Resolution

Within a single Arena, at Clash:

1. Compute each side's **Projected Force**: the sum of Force across their Units in this Arena, with the following modifications:
   - Units with **Deployment Sickness** contribute 0 Force (see §10.1). Blitz overrides.
   - Units with **Sentinel** contribute 0 Force to Projected Force (their Force is added to Fortification instead; see §11).
   - Other keyword or effect modifiers apply as printed.
2. Compute each side's **Fortification**: the sum of Ramparts across their Units in this Arena, plus the Force of any Sentinel Units on that side, plus any other effect-based modifiers.
3. Each side's **Incoming** = opponent's Projected Force − their own Fortification, minimum 0.
4. Apply Incoming as damage to the defending player's Conduit in that Arena (reducing its Integrity by that amount).
5. Units are not otherwise affected by Clash. Ramparts and Force are not consumed; Units do not die in Clash.

### 7.2 Units Do Not Die in Clash

This is the core combat-model break from MTG and its descendants. Units persist through combat by default. They are removed by:

- Direct destruction effects (Maneuvers, abilities).
- Damage reducing their Ramparts to 0 (see §7.3).
- Their controller's Conduit in their Arena being destroyed (see §7.4).
- Self-sacrifice costs.
- Specific keywords (e.g., **Phantom**).

Units are long-lived board presence. This means removal cards are the primary currency of tempo, and over-committing to one Arena has real costs.

### 7.3 Damage

Direct damage is dealt by Maneuvers, abilities, and Clash Incoming. It is governed as follows:

- **Damage to a Unit** reduces its current Ramparts by the damage amount. If a Unit's current Ramparts reach 0, the Unit is destroyed (sent to its owner's Cache, unless modified by **Recur** or similar).
- **Damage to a Conduit** reduces its Integrity by the damage amount.
- **Excess damage does not carry over.** Damage beyond a Unit's current Ramparts or a Conduit's Integrity has no further effect.
- **Ramparts heal at end of turn.** At the end of their controller's Fall phase, every Unit's current Ramparts are restored to their current maximum (printed Ramparts plus any ongoing modifiers such as Fortify). Permanent stat reductions (e.g., -1/-1 counters, if such effects exist in a given set) are not removed.

Force has no HP-like role; it is never reduced by damage. A Unit is destroyed only when its Ramparts reach 0 or when an effect explicitly destroys it.

### 7.4 Conduit Destruction

When a Conduit's Integrity reaches 0:

- That Conduit is destroyed. Its side of that Arena **Collapses** for its controller.
- All of the destroyed Conduit's controller's Units and Standards attached to that Arena are sent to their Cache.
- The opposing player's Units and Standards in that Arena remain. Their Units in that Arena no longer produce Incoming (there is no Conduit to damage) but they may still be relevant for effects that reference Units, Arenas, or Resonance.
- The player whose Conduit was destroyed may not deploy new Units or attach new Arena-Standards to that Arena for the rest of the game.
- The opposing player may still deploy Units and attach Arena-Standards in that Arena normally. If the opposing player's Conduit in this same Arena is later destroyed, that Arena is fully Collapsed for both players — no further Units may be deployed there by either player.

### 7.5 Victory

A player **wins** the moment two of their opponent's three Conduits are destroyed. This is checked any time Integrity changes. If both players meet the win condition on the same Clash or the same effect, the game is a **Draw**.

A player also **loses** if they must draw a card from an empty Arsenal (see §5). If both players would lose in the same window, the game is a Draw.

---

## 8. Resonance — The Faction System

This is the heart of the design.

### 8.1 The Resonance Field

Each player has a public row of five **Echo slots**, initially empty. This row is the **Resonance Field**.

Every time you play a card **from your hand**, you **Push** its faction into your Field:

- Add a new Echo on the right end with that card's faction.
- If you now have more than 5 Echoes, the **leftmost** (oldest) Echo falls off and is forgotten.

Dual-faction cards Push **both** factions as a single Echo that counts once toward each (see §9.7). Neutral cards Push a Neutral Echo (which does not count toward any faction but does occupy a slot and displace older Echoes). Tokens and copies do not Push (see §6.4).

**You cannot manually manipulate the Field.** It is strictly a record of your last 5 plays from hand. A handful of rare cards have explicit text that reshapes the Field (e.g., "reset your Resonance Field" or "duplicate your rightmost Echo"); these are costly and deliberate.

### 8.2 Resonance Tiers

Cards reference Resonance like this:

> **EMBER 2:** deal 1 damage to a Unit.
> **EMBER 4:** deal 3 damage to a Unit or Conduit instead.
> **Peak EMBER:** deal 5 damage, split as you choose.

A tier activates when your current Field has *at least* that many Echoes of the named faction, at the moment the card is played — specifically, step 4 of §6.2, *before* the new Echo is Pushed.

**Tiers labeled as alternatives of the same effect** (typically phrased with "instead") are mutually exclusive: the single highest satisfied tier resolves, and the others do not. In the example above, all three lines are alternatives of the same damage effect — only the highest applies.

**Separate gated lines on the same card are independent.** If a card reads:

> **EMBER 2:** Draw a card.
> **EMBER 4:** Deal 3 damage to a Unit or Conduit.

…then at EMBER 4+, both fire. The card's author signals alternatives with "instead" or equivalent language.

**Peak** is shorthand for "all 5 Echoes are this faction." It is the most powerful tier a card can offer, and cards with Peak effects are designed so the Peak text is genuinely game-bending — the reward for being all-in.

Dual-faction cards can offer tiers for either of their factions. Neutral cards may offer a **Banner** tier (see §8.3).

### 8.3 Banner

Your **Banner** is the faction with the single highest count in your current Resonance Field. Ties are broken by the most-recent Echo among tied factions. If your Field is empty or contains only Neutral Echoes, you have **no Banner**.

Banner is a *separate* mechanical hook from Resonance tiers. Cards may read:

> **If your Banner is THORN:** this Unit enters with +1/+1.

Banner-referencing text typically appears on Neutral or dual-faction cards, which want to scale with whatever you are committing to without prescribing a faction.

### 8.4 Designing Around the Field

Two consequences to internalize:

- **Order matters.** Casting a THORN card just before a "THORN 4" card is mechanically different from casting it after, even in the same turn.
- **Off-faction plays carry cost.** Every off-faction card pushes one of your on-faction Echoes toward falling off. This is the pressure that makes splashing real without banning it.

---

## 9. Factions

Five factions. Each has a distinct playstyle and two or three signature keywords. This section is overview; see the Supplement for identity details and representative cards.

### 9.1 EMBER — Aggression and Damage

- **Feel**: fast, committal, punishes slow Conduits.
- **Shape**: cheap Units with high Force and low Ramparts; Maneuvers that deal direct damage; escalating burn at higher tiers.
- **Signature keywords**: **Surge**, **Blitz**, **Ignite**.

### 9.2 BULWARK — Defense and Attrition

- **Feel**: slow, patient, wins late.
- **Shape**: high-Ramparts Units; Conduit healing; hard removal.
- **Signature keywords**: **Fortify**, **Mend**, **Sentinel**.

### 9.3 TIDE — Transformation and Control

- **Feel**: tempo via bouncing, reshaping, and replaying.
- **Shape**: Units that return to hand, Maneuvers that move other Units between Arenas, Resonance reshaping.
- **Signature keywords**: **Drift**, **Recur**, **Reshape**.

### 9.4 THORN — Growth and Swarm

- **Feel**: wide board, scaling midgame, commitment-hungry.
- **Shape**: token generation, per-turn buffs, effects that count Units.
- **Signature keywords**: **Rally**, **Sprawl**, **Kindle**.

### 9.5 HOLLOW — Disruption and Deception

- **Feel**: attacks hand, Resonance, and information.
- **Shape**: discard, Echo-manipulation (occasionally), flicker/phase effects.
- **Signature keywords**: **Phantom**, **Shroud**, **Pilfer**.

### 9.6 NEUTRAL

Neutral cards provide generic glue: modest stat-lines, utility, Banner-conditional effects. A Neutral card Pushes a Neutral Echo — it does not count toward any faction, but it does occupy a Field slot and displaces older Echoes. Neutral cards exist to be playable in any deck without actively *dragging* Resonance toward a different faction.

### 9.7 Dual-Faction Cards

A small number of cards have two factions (e.g., EMBER/THORN). These:

- Push **both** factions as a single Echo that counts once per faction.
- May satisfy tier effects of either faction.
- Cost slightly more than their single-faction equivalents for the same effect, reflecting their flexibility.

Dual cards are intentionally rare (target ~5% of any given set) so that the base five-faction identity stays crisp.

---

## 10. Card Types — Detail

### 10.1 Units

- Deploy to a chosen Arena; they stay there unless an effect moves them.
- Have Force and Ramparts.
- Contribute to Clash as described in §7.
- A Unit cannot change Arenas except via effects like **Drift**.
- There is no hard cap on Units per Arena, but some effects care about Arena crowding.

**Deployment Sickness.** A Unit deployed this turn has Deployment Sickness: it projects 0 Force at Clash on the turn it was deployed. Its Ramparts are unaffected (so it can still fortify). Deployment Sickness ends at the beginning of the Unit's controller's next Rise.

Deployment Sickness is overridden by **Blitz** and by any effect that explicitly says the Unit "enters without Deployment Sickness." A Unit that re-enters play (e.g., via Phantom returning to hand and being replayed) gains Deployment Sickness again as a fresh deployment.

### 10.2 Maneuvers

- Resolve immediately, then go to the Cache.
- May target Units, Conduits, Arenas, or players as printed.
- Cannot be played during Clash, Fall, or on the opponent's turn, unless they have **Interrupt**.

### 10.3 Standards

- Enter play attached to an Arena or to the player (as printed).
- Have ongoing effects until destroyed or sacrificed.
- Standards have no Force or Ramparts and cannot be damaged. They are destroyed only by effects that specifically target Standards or Permanents (e.g., "destroy target Standard," "destroy target Permanent"), or by meeting a printed self-destruct condition.
- A Standard attached to an Arena is destroyed if its controller's Conduit in that Arena is destroyed (its Arena side Collapses; see §7.4).
- A Standard attached to the player remains until destroyed.

---

## 11. Keyword Glossary (Core)

Only the most important keywords are defined here; the Supplement has the full list.

- **Surge** *(EMBER)*: When you play this, if you have Pushed another EMBER Echo this turn, this costs 1 less Aether (minimum 0).
- **Blitz** *(EMBER)*: This Unit ignores Deployment Sickness; its Force contributes to Clash on the turn it is deployed.
- **Fortify X** *(BULWARK)*: This Unit's Ramparts are increased by X while your Conduit in this Arena has 4+ Integrity.
- **Mend X** *(BULWARK)*: When this enters play, heal the Conduit in this Arena by X, up to its starting Integrity. If the relevant Arena is Collapsed for your side, Mend fizzles.
- **Sentinel** *(BULWARK)*: This Unit's Force is added to your Fortification in this Arena instead of your Projected Force. Sentinel Units do not project Force at Clash.
- **Drift** *(TIDE)*: At end of your turn, you may move this Unit to an adjacent Arena (see glossary for adjacency). A Unit cannot Drift into an Arena that is Collapsed for its controller.
- **Recur** *(TIDE)*: When this is destroyed, return it to the bottom of your Arsenal instead of sending it to the Cache.
- **Reshape** *(TIDE, on Maneuvers)*: Before this resolves, you may swap two adjacent Echoes on your own Resonance Field. Does not affect the opponent's Field.
- **Rally** *(THORN)*: When another friendly Unit enters this Unit's Arena, this Unit gains +1 Force until end of turn.
- **Sprawl X** *(THORN)*: When you play this, create X 1/1 Thorn Saplings in this Arena. Saplings are Units; they are NEUTRAL-factioned; they enter with Deployment Sickness; they do not Push Echoes (they are not played from hand).
- **Kindle** *(THORN, on Standards)*: At the start of your turn, place a Kindle counter on this Standard. Once a Standard has 3 Kindle counters, its Kindle effect fires and counters reset to 0.
- **Phantom** *(HOLLOW)*: At Start of Clash, this Unit's controller may declare Phantom. A Phantoming Unit projects 0 Force this Clash, does not contribute to Fortification, and returns to its owner's hand at End of Clash. Its Aether cost is permanently reduced by 1 (cumulative across multiple Phantom triggers, minimum 0) while it resides in hand, Arsenal, or Cache; this discount is tracked on the card.
- **Shroud** *(HOLLOW)*: Cannot be chosen as a target by opposing Maneuvers or abilities. Effects that affect all Units (sweepers) are not blocked by Shroud. Your own targeted effects pass through Shroud freely.
- **Pilfer X** *(HOLLOW)*: When this enters play, your opponent reveals their hand. You choose up to X cards and exile them to the bottom of their Arsenal. If the opponent has fewer than X cards in hand, you exile as many as are available.

**Peak** is not a keyword; it is a tier label meaning "all 5 Echoes of the named faction."

**Interrupt** is a keyword, defined in §6.3.

---

## 12. Example of a Turn

*(Illustrative — cards referenced are defined in the Supplement.)*

Round 4. Priya's Resonance Field reads `EMBER EMBER THORN EMBER EMBER`. Her Banner is EMBER (4 Echoes), with 1 THORN.

- **Rise**: Priya refreshes to 6 Aether. Her Units' Ramparts clear damage and reset. She draws *Smolder*, an EMBER Maneuver (cost 2) reading: "Deal 2 damage. **EMBER 4:** Deal 4 instead."
- **Channel**:
  - She plays *Smolder*, paying 2 Aether, targeting the opponent's Center Conduit. She currently has 4 EMBER Echoes; EMBER 4 is satisfied, so the alternative fires: 4 damage to the Center Conduit. *Smolder* resolves, then Pushes an EMBER Echo; her Field becomes `EMBER THORN EMBER EMBER EMBER`. (4 Aether remaining.)
  - She plays *Cinderling*, an EMBER 2/1 Unit with Blitz, cost 1. It enters the Center Arena. Push EMBER. Field becomes `THORN EMBER EMBER EMBER EMBER` — she still has 4 EMBER, not Peak. (3 Aether remaining.)
  - She plays *Stokefire*, an EMBER Maneuver (cost 3) reading "Deal 3 damage, split among any number of targets. **Peak EMBER:** Deal 5 damage, split as you choose." Her Field is currently 4 EMBER + 1 THORN — not yet Peak. The base effect fires: she deals 3 damage to an opposing HOLLOW Unit with Ramparts 3, destroying it. *Stokefire* resolves, then Pushes EMBER. Field is now `EMBER EMBER EMBER EMBER EMBER` — she is now at Peak, but the card that brought her there has already resolved. (0 Aether remaining.)
- **Clash** (Center Arena): Her Cinderling (Blitz, 2/1) projects 2 Force. Her opponent has 0 Force in Center. Incoming = 2. Center Conduit takes 2 more damage. Total Center Conduit damage this turn: 4 + 2 = 6.
- **Fall**: No end-of-turn effects.
- **Pass**.

Next turn, any EMBER card she plays will see the Peak tier — until the first non-EMBER card she plays shifts her off Peak.

---

## 13. Formats

### 13.1 Constructed

- Arsenal size: **exactly 40 cards**.
- Copy limit: **up to 3 copies** of any card except Uniques (marked, limit 1).
- No sideboard in base rules. Tournament variants may use a 5-card sideboard.
- No banned cards at launch; a living ban list is expected post-release.

### 13.2 Draft

- Format: **Three packs of 15 cards.**
- Pack distribution target: 10 commons, 3 uncommons, 1 rare, 1 foil-slot (any rarity, draft-only chase).
- Draft order: Pack 1 passes left, Pack 2 passes right, Pack 3 passes left.
- Players draft 45 cards. Build an Arsenal of **exactly 30 cards** from their drafted pool plus unlimited copies of a generic "Baseline" Unit (1-cost 1/1 NEUTRAL filler, provided by the format). Baseline cards are an exception to the normal Push rule: they do not Push any Echo (not even Neutral).
- Copy limits do not apply in Draft.
- Resonance works identically in Draft. Sets are designed so that forcing a two-faction lean is viable and Peak is reachable but not expected.

### 13.3 Draft Design Constraint (the designer's note)

A set must be draftable such that a 30-card deck with 18+ cards of one faction is consistently achievable by someone who reads signals well. The Supplement discusses the density targets and common/uncommon distributions that support this.

---

## 14. Glossary

- **Active player**: the player whose turn is happening.
- **Adjacent Arena**: Left and Center are adjacent; Center and Right are adjacent; Left and Right are *not* adjacent.
- **Cache**: discard pile.
- **Collapsed Arena (side)**: a player's side of an Arena where their Conduit has been destroyed; that player may no longer deploy Units or attach Arena-Standards there.
- **Debt**: Aether owed to your next turn, incurred by playing Interrupts on the opponent's turn.
- **Deployment Sickness**: a fresh Unit projects 0 Force at Clash on its deployment turn. Overridden by Blitz. See §10.1.
- **Destroy**: send to the Cache (or, if an effect says so, exile).
- **Echo**: one slot in the Resonance Field.
- **Exile**: remove from play without entering the Cache; generally permanent.
- **Friendly / Opposing**: relative to the active player or the effect's owner.
- **Peak**: 5-of-5 Echoes of a specified faction.
- **Permanent**: a Unit or Standard currently in play. Conduits are not Permanents; they are their own category and targeted by their own language (e.g., "target Conduit").
- **Push**: add an Echo to the right end of your Resonance Field.
- **Resolve**: carry out a card or ability's effect.
- **Tier**: a Resonance threshold on a card (e.g., "EMBER 3").
- **Token**: a Unit created by an effect rather than played from hand. Tokens do not Push Echoes.

---

## 15. What This Game Is *Not*

To be explicit about the design space we are deliberately avoiding:

- **No land or resource-card system.** Aether scales deterministically.
- **No tap-to-attack.** Force is always on; Units are not exhausted.
- **No combat-triggered creature death.** Units survive Clash by default.
- **No interrupt stack.** Only Interrupt-keyword Maneuvers respond on the opponent's turn.
- **No declared deck faction or color identity.** The Resonance Field is the only thing that tracks who you are, and it updates every play.
- **No "one land per turn" style curve-lock.** Every card is playable on its cost-turn.
- **No Force-as-HP model.** Force is purely offensive; Ramparts are defensive and HP-like. Damage to Units only reduces Ramparts.

These absences are the backbone of the game feel. If a future mechanic threatens one of them, it should be weighed very carefully.
