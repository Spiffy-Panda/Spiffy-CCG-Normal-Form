# Prompt - Claude Opus 4.7 
Lets explore making a collectable card game. 

I want you to be the main designer. The constraint that I want to impose is that there is a faction system, it is a soft faction system (you don't declare it, you can splash into other factions, but something pushes you to limiting your factions) and can be played both constructed and draft. 

First draw up the rules, do not think-about/speak of implementation details yet. There should be two documents a primary one that focuses on the rules of the game and a supplement that stores example cards.

# Conduit — Core Rules

*A two-player collectable card game with a soft-faction system playable in both constructed and draft.*

---

## 1. Design Pillars

- **Commitment over declaration.** You never pick a faction at deckbuild or at game start. What you play creates your identity as the game unfolds.
- **Splash is real, not free.** Mixing factions always works. It just doesn't unlock everything.
- **Draft and constructed share DNA.** The same mechanic (Resonance) that rewards deep decks in constructed also rewards drafting on-color signals in a limited pool.
- **One resource, no mana screw.** A clean tempo curve so that losses feel earned and wins feel deserved.

---

## 2. Overview

Two players, each representing a **Conduit** channeling the raw magic of the world through one or more of five traditions, battle to reduce the opponent's **Life** (starting at 25) to zero.

Each player brings a deck. Turns alternate. Cards cost **Energy**, a scaling resource. Units fight; tactics resolve; relics persist. The player who manages their resources, Resonance, and board best wins.

### 2.1 Win Conditions

You win when any of the following is true of your opponent:

1. Their **Life** is 0 or less.
2. They must draw a card from an empty deck.
3. A card effect explicitly grants you the game, or causes them to lose it.

If both players simultaneously meet a loss condition, the game is a **draw**.

---

## 3. The Five Factions (and Neutral)

Each non-neutral card belongs to exactly one **Faction**, marked by a colored sigil in its frame. The five factions are thematic "schools of channeling" and define the game's strategic poles.

| Faction | Archetype Identity | Key Gameplay Pulls |
|---|---|---|
| **Pyre** | Aggression and damage | Cheap Swift units, direct damage, reckless combat tricks |
| **Verdance** | Growth and persistence | Energy ramp, large bodies, recursion from discard |
| **Rime** | Knowledge and manipulation | Card draw, bounce, reactive counters, evasive flyers |
| **Umbra** | Sacrifice and return | Removal, discard fuel, reanimation, slow inevitability |
| **Bastion** | Order and endgame | Damage prevention, healing, taxing threats, tall bodies |

**Neutral** cards bear no faction and can never contribute to Resonance (see §6). They are typically utility or flexible threats and are the primary tools a splash-heavy deck uses to hold itself together.

**Dual-faction cards** (rare; uncommon-slot "signposts" in draft) belong to two factions simultaneously. They count toward Resonance for both factions and can be played in any deck — but they are only strong when both factions are present in meaningful amounts. Dual-faction cards never have a Resonance clause of their own.

---

## 4. Card Anatomy

Every card shows:

- **Name**
- **Faction sigil** (one of five, Neutral, or a dual combination)
- **Energy Cost** (a single number)
- **Type**: Unit, Tactic, or Relic
- **Subtype**: flavor/tribal tag (e.g., *Beast*, *Warrior*, *Construct*) — referenced by some abilities
- **Power / Health** (Units only)
- **Text box**: keywords, triggered abilities, and any **Resonance** clauses
- **Rarity**: Common, Uncommon, Rare, Mythic

---

## 5. Zones

- **Deck**: your face-down library.
- **Hand**: your private cards. Max hand size 8 at end of turn; discard the excess.
- **Battlefield**: where Units and Relics live.
- **Discard**: a public, face-up pile. Used Tactics and destroyed Units land here. **This is what Resonance reads.**
- **Void**: the exile zone. Cards here are removed from play and do not count for Resonance.
- **Stack**: a temporary zone for cards that are currently resolving; see §9.

---

## 6. The Resonance System *(core mechanic)*

Some cards include a clause written as:

> **Resonance [N]:** [bonus effect]

When this card **resolves**, count the cards in your Discard that share its faction. If that count is **N or greater**, the bonus effect happens in addition to the card's base effect. Otherwise, the card resolves normally.

### 6.1 Rules for counting Resonance

1. The card currently resolving **does not count itself** (it hasn't reached your Discard yet).
2. Only cards in **your** Discard pile count — not your opponent's, and not the battlefield or Void.
3. A card's Resonance clause always checks **that card's own faction**. A Pyre card checks Pyre cards in your Discard, regardless of what's also there.
4. Dual-faction cards count for **both** factions wherever they sit in Discard.
5. Neutral cards never count for any Resonance total and never have Resonance clauses.
6. A single card may have multiple Resonance clauses at different thresholds; each is checked independently.
7. Resonance is re-evaluated **once, on resolution**. Changing what's in your Discard afterward does not change a past Resonance result.

### 6.2 Why this works as a soft-faction system

- **Mono-faction** decks light up Resonance on almost every relevant spell from the mid-game onward. Payoffs are large and reliable.
- **Two-faction** decks commit to one faction as the "core" (cheap enablers, payoff engine) and the other as "support" (one or two key splashes whose Resonance may or may not be live).
- **Three-plus-faction** decks lose their Resonance game almost entirely, so the splashed cards must be strong *without* their bonus — they must be doing something structural, not something explosive.

No one is ever forbidden from adding a card. The system simply doesn't hand out the payoff portion unless you've paid for it with deck commitment.

---

## 7. Resources: Energy

The game has one resource. Every player has:

- An **Energy Cap** (maximum for the turn)
- A current **Energy** pool

### 7.1 Energy progression

- Turn 1: your Energy Cap is **1**.
- At the start of every subsequent turn, your Energy Cap increases by **1**, up to a maximum of **10**.
- At the start of each of your turns, your Energy pool refills to your current Cap.
- Spending Energy reduces the pool. Unspent Energy does not carry over across turns.

There is no separate mana card, no color-fixing concern at the resource layer, and no variance in how quickly either player ramps. Any "ramp" effects are explicit card abilities (mostly Verdance) and must be earned by playing those cards.

---

## 8. Turn Structure

Each turn proceeds through six phases in order:

**1. Start Phase**
- Increase your Energy Cap by 1 (skip on turn 1); refill your Energy pool.
- Resolve "at the start of your turn" triggers.
- Ready all your Exerted permanents.
- Each of your Units loses the **Stalled** state (see §9.4).

**2. Draw Phase**
- Draw 1 card. (The player who goes first **skips** this step on their very first turn to mitigate the going-first advantage.)

**3. Main Phase I**
- Play Units, Tactics, and Relics you can afford.
- Activate abilities.

**4. Combat Phase** (optional; see §9)

**5. Main Phase II**
- Same as Main Phase I.

**6. End Phase**
- Discard down to the 8-card hand limit.
- Resolve "at end of turn" triggers.
- Any "until end of turn" effects expire.

The non-active player may play **Reactive** cards (see §11) during certain steps.

---

## 9. Combat

The active player decides whether to enter Combat. If they do, it proceeds in ordered steps.

### 9.1 Declare Attackers

The active player chooses any number of ready, non-Stalled Units they control and declares them as attackers. Attackers become **Exerted** (turned sideways). Each attacker automatically targets the opposing player.

Before attacks are locked in, the defending player may play Reactive cards.

### 9.2 Declare Blockers

The defending player chooses how to assign their ready Units as blockers. Each blocker may block exactly one attacker. Each attacker may be blocked by any number of blockers (including zero).

If an attacker is blocked by multiple blockers, the **attacking player** chooses the order in which damage is assigned among them.

Before blocks are locked in, the active player may play Reactive cards.

### 9.3 Deal Damage

All combat damage is dealt **simultaneously**.

- **Unblocked attacker**: deals its Power to the defending player as Life damage.
- **Blocked attacker**: deals its Power as damage, distributed in the chosen order among its blockers. Each blocker deals its Power as damage back to the attacker.
- A Unit dealt damage equal to or greater than its Health is destroyed and put into its owner's Discard.
- If an attacker has **Pierce** and deals more damage than the total Health of all its blockers, the excess spills over to the defending player.

### 9.4 The Stalled state

A Unit that entered the battlefield **this turn** is **Stalled**. A Stalled Unit cannot attack and cannot be used to pay Exert costs on abilities. It may still block normally.

The keyword **Swift** removes the Stalled state for the Unit that has it.

---

## 10. Playing Cards and the Stack

When a card is played:

1. Announce the card and its targets (if any).
2. Pay its Energy cost.
3. The card goes onto the **stack**.
4. Both players may respond by playing **Reactive** cards in alternating priority; each response also goes onto the stack.
5. The stack resolves top-down: the last card added resolves first. Each card's effects (and Resonance clauses) are evaluated at the moment it resolves.

Non-Reactive cards may only be played on the active player's Main Phases.

Tactics go to Discard when they finish resolving. Units and Relics enter the battlefield.

---

## 11. Keywords

The following keywords appear across the cardpool. Faction affinity is shown for flavor, but any keyword can appear on any card if the design calls for it.

- **Swift** *(Pyre-leaning)* — This Unit ignores Stalled the turn it enters play.
- **Pierce** *(Pyre, Verdance)* — Excess combat damage dealt to blockers is dealt to the defending player.
- **Flight** *(Rime, Pyre)* — Only Units with Flight or Reach may block this Unit.
- **Reach** *(Verdance, Bastion)* — This Unit may block Units with Flight.
- **Lifedrink** *(Umbra, Bastion)* — Damage this Unit deals also restores that much Life to you.
- **Ward N** *(Bastion, Rime)* — The first N damage dealt to this Unit each turn is prevented.
- **Vanish** *(Rime, neutral)* — When this leaves play, send it to the Void instead of Discard. (It will not count for Resonance.)
- **Echo** *(Umbra)* — When you play this Tactic, place a copy of it in your Void. (Mostly used as a reanimation target.)
- **Forge *(cost)*** *(Verdance, Bastion)* — Exert this permanent and pay *(cost)*: activated ability.
- **Reactive** — This card may be played at any time that any player has priority, including during the opponent's turn and during combat steps.
- **Legendary** *(any faction)* — You may control only one copy at a time; additional copies are put into the Discard as they enter play.

Keywords interact cleanly: *Lifedrink + Pierce*, for example, means excess damage to the defending player still heals you for that amount.

---

## 12. Constructed Format

- **Deck size:** exactly **40** cards.
- **Copy limits:** up to **3** copies of any one card, except **Legendary** cards (limit **1**).
- **Faction limits:** none. You may include any combination of factions and Neutrals.
- **Sideboard:** 5 cards for best-of-three, swappable 1-for-1 between games.
- **Life:** 25.

Deckbuilding note: because Resonance counts your own Discard, Tactics contribute to Resonance the moment they resolve. Unit-heavy decks build Resonance more slowly because units must die first. This naturally pushes Tactic-heavy faction shells to feel "fast on Resonance" while Unit-heavy shells feel "late on Resonance" — a deliberate design axis.

---

## 13. Draft Format

- **Pod size:** 8 players.
- **Packs:** each player opens **3 booster packs** of **15 cards**.
- **Rotation:** picks alternate direction each pack (left, right, left).
- **Deck size:** exactly **30** cards built from your drafted pool, plus any number of "Basic Neutrals" supplied from a shared pool (see below). You may not draft then return cards; the 30-count is from your own picks.
- **Life:** 25.

### 13.1 Pack composition (baseline)

- 1 rare or mythic
- 3 uncommons
- 10 commons
- 1 guaranteed **Neutral** common (the "splash slot")

### 13.2 Dual-faction signposts

The uncommon slot frequently contains **dual-faction "signpost" cards**, each of which shouts "draft this pair." There is one signpost per two-faction pair (10 pairs total). Signposts are never themselves Resonance cards — they are already contingent on having two factions present.

### 13.3 Basic Neutrals

To keep drafts from collapsing into unplayable mana-less piles, each drafter may fill any number of deck slots with **Basic Neutral** cards from a shared pool. These are deliberately weak filler: a vanilla 1/1 for 1, a do-nothing 2/2 for 2, etc. Their purpose is to ensure nobody is forced to run a card they hate just because they're short.

### 13.4 Resonance in draft

A 30-card deck that is roughly 18/12 between two factions can comfortably reach Resonance [3] by turn 4 in its main faction, Resonance [4–5] by turn 6–7, and Resonance [5+] rarely. Card designers therefore tune Resonance numbers so that:

- **Resonance [2–3]** = "this hits in a normal two-color draft"
- **Resonance [4]** = "this hits if you committed"
- **Resonance [5+]** = "this is a mono-faction reward"

Common Resonance is usually [2] or [3]. Rares and mythics can reach [5+] with proportionally huge payoffs.

---

## 14. Mulligans

Each player draws **5** cards. Either may, in turn order, mulligan: shuffle their hand back and redraw with **one fewer** card. You may mulligan down to 3. No "scry" or "bottoming" — the mulligan is a hand-size cost only.

---

## 15. Priority & Timing (summary)

- On your turn's Main Phase, you have priority. You play cards and activate abilities.
- Between any two of your plays, your opponent may play a Reactive card.
- During combat, priority passes between players at the start and end of each substep (declare attackers, declare blockers, damage).
- A card or ability on the stack resolves only after both players decline to respond with priority.

---

## 16. Glossary

- **Active player**: the player whose turn it is.
- **Defending player**: the non-active player during combat.
- **Exert / Ready**: sideways / upright orientation. An Exerted permanent has used its activation for the turn (attacking counts).
- **Stalled**: temporarily unable to attack or Exert; applies to Units entering this turn. Cleared at Start Phase.
- **Discard**: your graveyard zone. Resonance reads from here.
- **Void**: the exile zone. Cards here do not count for Resonance.
- **Faction**: Pyre, Verdance, Rime, Umbra, Bastion, or Neutral.
- **Resonance [N]**: a conditional bonus evaluated at resolution; triggers if ≥ N cards of the same faction are in your Discard.
- **Swift, Pierce, Flight, Reach, Lifedrink, Ward, Vanish, Echo, Forge, Reactive, Legendary**: see §11.

---

## 17. First-turn summary (quick reference)

> Draw 5 (mulligan once if desired). Coin flip for first player. On turn 1 the first player skips their Draw step. Gain 1 Energy Cap, play at most one 1-cost card, done. Hand to opponent.

---

*See `CARDS.md` for example cards illustrating every faction, the Resonance system at multiple thresholds, Neutral utility, and dual-faction signposts.*
