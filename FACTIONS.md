# Conduit — Seven-Faction Rework

*Supersedes §3 of `RULES.md` and updates the keyword list in §11. Integrates with §6 (Resonance) and §13 (Draft) unchanged in rule, but with retuned numbers.*

---

## 0. What changed and why

The original five factions map cleanly to the MtG color pie. That's a tested design, but it leaves two strategic archetypes unserved:

- **Wide boards / collective play.** No existing faction is meaningfully interested in "many small units instead of few large ones." Verdance can do it by accident with Rootspeaker lines, but it's not the pull.
- **Modal / adaptive play.** No existing faction cares about *choice at play-time*. Rime manipulates the deck, but it doesn't make your cards flexible in the moment.

Those are the two new poles. I've kept the other five and renamed/retuned them — the names are shorter and less epic-fantasy-coded, the identities are slightly tightened, and two keywords got rebranded for consistency.

**Cost of the change** (you should see this up front):

| Count | 5-faction | 7-faction |
|---|---|---|
| Factions | 5 | 7 |
| Dual-faction pairs | 10 | **21** |
| Signpost uncommon slots per set | 10 | **21** |
| Share of pack per faction (baseline) | ~20% | ~14% |
| Resonance [3] hit-rate in 2-color draft | comfortable | **needs retune** |

The 21 signposts is the real load-bearing cost. I address it in §4 below.

---

## 1. The Seven Factions at a glance

| Faction | One-line identity | Strategic pole | Keyword affinity |
|---|---|---|---|
| **Ember** | Violent speed | Aggro, burn | Swift, Pierce |
| **Loam** | Rooted inevitability | Ramp, recursion | Pierce, Reach, Forge |
| **Tide** | Knowledge and refusal | Control, tempo | Flight, Ward, Reactive |
| **Shade** | Loss as fuel | Attrition, reanimator | Siphon, Echo, mill |
| **Keep** | Endurance as victory | Prison, lifegain | Ward, Siphon, Forge |
| **Flock** | Many instead of one | Wide boards, tokens | Kin, Swift |
| **Drift** | Becoming, not being | Modal, adaptive | Shift, Fade |

Ember, Loam, Tide, Shade, Keep are the rename-and-retune of Pyre, Verdance, Rime, Umbra, Bastion. **Flock** and **Drift** are new.

---

## 2. Faction detail

### Ember — Violent speed

*Short fuses. Shorter tempers. Everything worth doing is worth doing before someone can stop you.*

Ember is the face-first aggression faction. Cheap units with Swift, cheap removal pointed at face or small blockers, reckless combat tricks that trade long-term value for immediate pressure. Ember's Resonance rewards dumping tactics fast — a mono-Ember deck can reach Resonance [3] by turn 3 on a one-drop, one-drop, two-drop curve, and every subsequent spell is a little bigger than its rate suggests.

Ember does **not** do: card advantage, scaling late-game threats, flexibility. It's the faction that loses if it doesn't win by turn 8.

**Tension points**: Loam outsizes it; Keep outlasts it; Tide blanks it. Flock goes wider than it (interesting rivalry). Shade trades with it. Drift can blunt it with Fade.

---

### Loam — Rooted inevitability

*The forest doesn't beat you quickly. It beats you completely.*

Loam ramps Energy, plays large bodies, and returns things from Discard to hand. Its core win condition is "by turn 8, my threats are bigger than your answers." Resonance rewards Discard density, which Loam builds naturally through Tactic-heavy ramp spells in the early game.

This is the faction most likely to reward explicit **Forge** abilities — exert-and-pay activated abilities on big creatures that turn durable bodies into engines.

**Tension points**: Tide outpaces and counters it; Ember races it; Shade steals its threats. Against Flock it's a matchup of "one big vs many small" where Pierce and Reach matter enormously.

---

### Tide — Knowledge and refusal

*Every argument you're about to make, we've already read.*

Tide is the reactive control faction. It draws cards, bounces threats to hand, counters tactics, and wins with evasive flyers once the game has gone long enough that it has more resources than the opponent. Resonance rewards drawing and discarding — Tide naturally floods its own graveyard through card selection.

Tide's key constraint: almost everything it does happens on the opponent's turn. That makes it slow in Main Phases but disproportionately strong during combat.

**Tension points**: Ember burns under its counter window; Flock outpaces its spot answers; Shade grinds it out in late game when Tide runs out of reactive pieces. Drift is an interesting mirror — both factions care about flexibility.

---

### Shade — Loss as fuel

*The dead are still working. You just can't see them clearly.*

Shade uses Discard as a resource pile. It mills itself, drains opponent life with Siphon, and reanimates. Its Resonance threshold is easiest to hit of any faction — self-mill pumps its own counter. In exchange, Shade's cards are individually weaker for their cost; the cost is paid in card efficiency, not Energy.

Shade is explicitly **not** evil or edgelord-coded. Its flavor is ancestor-work: the dead are kin, not enemies. Mechanically it's reanimation and attrition; thematically it's continuity.

**Tension points**: Keep gains life faster than Shade can Siphon; Tide counters its reanimation; Ember races it before its engine goes online. Against Drift, both factions care about graveyard manipulation but for opposite reasons.

---

### Keep — Endurance as victory

*The walls are not there to hurt you. The walls are there because the world is not sorry.*

Keep is the defensive prison faction. Ward, healing, tax effects, big late-game threats, damage prevention. It wins by outliving the opponent's clock, then closing with oversized single threats. Resonance rewards sustained presence — Keep's Discard fills slowly because its creatures are sticky, so its Resonance numbers skew lower than other factions' for the same payoff strength.

**Design note on Keep's tax effects**: these are the faction's most controversial piece. I've kept them (Hallowed Magistrate analog) but they need careful power-level tuning — "tactics cost more" effects can feel miserable to play against if overtuned. Keep them *uncomfortable* but not *oppressive*.

**Tension points**: Loam outsizes it in the mirror of long-game; Shade drains around its Ward; Flock overwhelms it with volume that taxes don't stop (you can only tax the first spell each turn). Drift can rebuild around its prison pieces with modality.

---

### Flock — Many instead of one *(new)*

*Alone, a thing is prey. Together, it is weather.*

Flock fills the go-wide gap. Its units are small (1-3 Power typical), cheap, come with Kin (see §3), and generate tokens. The faction's Resonance pays off for fielding a board, not a champion. A mono-Flock deck wants 5+ creatures on the battlefield by turn 5 and draws its payoff spells that scale per unit.

Mechanically Flock is the most cross-paired faction in the design — it runs well alongside almost anything, because "I have a lot of small creatures" plus "one good buff/ramp/removal faction" is a coherent shell in most directions.

**Flavor**: I've avoided "hive" or "swarm" language, both of which pull toward either bug-horror or fascist-collective tropes. Flock's imagery is *mutual-aid* coded: murmuration, salmon run, cooperative craft-guild. The small thing isn't a drone; it's a neighbor.

**Tension points**: Ember burns its small bodies two-for-one; Loam's Reach + Pierce blowouts are devastating against tokens; Tide's board-wipe tactics are catastrophic; Keep's damage prevention stops the plan cold. Shade recycles its dead kin. Drift can turn any of Flock's small bodies into bigger ones with Shift.

---

### Drift — Becoming, not being *(new)*

*You showed up with a plan. We showed up with seven plans.*

Drift fills the modality gap. Its cards use **Shift** (see §3): choose one mode on play, with Resonance unlocking additional modes. Its Units shift between forms, tactics bend to the situation, and its most expensive spells are essentially toolboxes.

The faction has no single-best gameplan. That is intentional. Drift's strength is that its hand is always a little more useful than the opponent expects, because its cards answer to the moment instead of to a script.

**Flavor**: Drift is the identity-fluid faction. Its characters are changelings, moon-witches, theatre-priests, river-shapers — magic-users for whom *being one thing* is considered the strange choice. This is the faction with the deliberate queer aesthetic, and the flavor text leans into self-determination without sermonizing it.

**Tension points**: Ember punishes slow decisions; Tide counters its key modes; Keep outlasts it; Shade recycles while it's still picking between options. Against Flock it's an interesting asymmetric matchup — Flock has volume, Drift has quality-of-choice.

---

## 3. Keyword rework *(updates §11)*

### Kept as-is

- **Swift** *(Ember, Flock)* — Ignores Stalled the turn it enters play.
- **Pierce** *(Ember, Loam)* — Excess combat damage spills to the defending player.
- **Flight** *(Tide, Ember)* — Only Flight or Reach may block this Unit.
- **Reach** *(Loam, Keep)* — May block Flight.
- **Ward N** *(Keep, Tide)* — First N damage each turn is prevented.
- **Reactive** — May be played at any priority point.
- **Forge *(cost)*** *(Loam, Keep)* — Exert this permanent, pay cost: activated ability.
- **Legendary** — Limit 1 on battlefield.
- **Echo** *(Shade)* — When you play this Tactic, place a copy in your Void. Primarily a reanimation target.

### Renamed (function unchanged)

- **Siphon** *(Shade, Keep)* — *was Lifedrink.* Damage this Unit deals also restores that much Life to you. Renamed for tone consistency and to de-gender/de-animal the flavor (Lifedrink read vampiric by default; Siphon is neutral and works across Shade's ancestor-work and Keep's cleric-magic).
- **Fade** *(Tide, Drift)* — *was Vanish.* When this leaves play, it goes to the Void instead of Discard. Rename is for voice: Fade suggests dissolution, which fits both Tide's mist aesthetic and Drift's shapeshift aesthetic better than Vanish.

### New

- **Kin** *(Flock)* — Whenever another Unit enters play under your control, this Unit gets +1/+0 until end of turn. A cascading board-wide effect that rewards playing the deck as intended. Clean and self-contained; doesn't create feedback loops.

- **Shift** *(Drift)* — When you play this, choose one of the listed modes. Cards with Shift usually present 2–3 options; Resonance clauses on Shift cards typically read *"Resonance [N]: choose one additional mode instead."* This couples Drift's identity to Resonance in a distinctive way — Drift's reward for committing is *more agency*, not just bigger numbers.

### Retired

None. Every keyword from the original list has a home.

### Keyword count

10 keywords (unchanged from original 11 if you count Echo, which I've kept; net 11 including Shift and Kin, minus Vanish/Lifedrink which are renames). Accessibility target is "a new player can read the reminder text once and remember the keyword" — all of these clear that bar.

---

## 4. Structural consequences of 7 factions

### 4.1 Resonance thresholds need retuning

In a 7-faction world with evenly-distributed packs, each faction is ~14% of the cardpool vs ~20% before. A two-faction draft deck has proportionally fewer on-faction cards to find. The Resonance tuning table from §13.4 should shift:

| Tier | 5-faction meaning | 7-faction meaning |
|---|---|---|
| Resonance [2] | Trivial in 2-color draft | Reliable in 2-color draft |
| Resonance [3] | Reliable in 2-color draft | Requires commitment |
| Resonance [4] | Committed | Heavy-commitment reward |
| Resonance [5+] | Mono-faction only | Mono-faction only, later-game |

Card designers should drop common Resonance thresholds by one tier across the set. Most commons that were [3] become [2]; most uncommons that were [3] become [3] still but read differently in practice.

### 4.2 The 21-pair problem

21 dual-faction signposts is a lot. Three mitigations:

**(a) Not every pair gets a signpost every set.** The original rules called for one signpost per pair per set. In a 7-faction world, rotate signposts: each set prints ~10–14 of the 21, with a 2-set cycle covering all pairs. Competitive players learn which pairs are "live" in the current meta.

**(b) Some pairs genuinely don't need signposts.** Pairs where the mechanical synergy is weak (Keep + Flock, say — defensive prison + go-wide is an awkward marriage) can skip the signpost and let the pair exist only in decks that build it deliberately. The rulebook should note this.

**(c) Signposts can be rares, not uncommons, for thin pairs.** An uncommon signpost is a "this archetype exists" announcement in draft; a rare signpost is "this archetype exists but you'll only build it if you open the rare." For the 6–8 least-supported pairs, rare-slot signposts are enough.

### 4.3 Neutral becomes more important

With 21 possible 2-color archetypes and more fragmented pack distribution, a larger share of every draft deck is Neutral. I'd expect Neutral to go from ~10% of a drafted deck to ~15–20%. That means Neutral needs more *variety*, not just more volume: better utility, not just filler. The Basic Neutrals in CARDS.md remain as last-resort filler; print Neutral uncommons (like Mercenary Captain) more generously.

### 4.4 Constructed will likely collapse to ~3 dominant pairs

Ignore this at your peril. In a 21-pair format, the competitive meta always consolidates to a small handful of pairs plus one or two three-color decks. This is fine — it's what happens in any multi-faction CCG. Your design obligation is to make sure *which* three pairs dominate varies by set, not to pretend 21 archetypes all stay live. Tune set-by-set with explicit intent about which pairs you're boosting.

---

## 5. Illustrative cards *(new mechanics only)*

Not a full cardset — just enough to anchor the two new keywords and the two new factions. Existing faction cards in CARDS.md port over under renames (Ember Gutter → Cinder Gutter, Pyre Carrion → Ember Carrion, etc.; no mechanical change).

### Flock (new faction)

**Fingerling** — 1 | Unit (Fish) | Flock | Common
*1 / 1* — Kin.
*"Alone they're nothing. Look at the shape of them together."*

**Murmuration** — 3 | Tactic | Flock | Common
Create two 1/1 Flock Unit tokens named Sparrow with Flight.
**Resonance [3]:** Create three instead.

**Guildmeet Captain** — 3 | Unit (Human) | Flock | Uncommon
*2 / 3* — Kin. When another Unit enters play under your control, that Unit gets +1/+0 until end of turn.
*Note: the Kin stacking is intentional — Guildmeet Captain buffs itself AND the new Unit.*

**The Salmon Run** — 5 | Tactic | Flock | Rare
Each Unit you control gets +1/+1 and Swift until end of turn.
**Resonance [4]:** Also create two 1/1 Flock Unit tokens before the buff applies.

**Weft-Weaver, Keeper of Threads** — 6 | Unit (Human, Legendary) | Flock | Mythic
*3 / 5* — Kin. Other Units you control have Kin.
**Resonance [5]:** When Weft-Weaver enters play, create X 1/1 Flock Unit tokens, where X is the number of Units you control.

### Drift (new faction)

**Two-Faced Answer** — 2 | Tactic | Drift | Common | Shift
Choose one:
- Deal 2 damage to a Unit.
- Return a Unit to its owner's hand.

**Shape of a Choice** — 3 | Unit (Changeling) | Drift | Uncommon | Shift
*2 / 2* — When this enters play, choose one:
- It gains Flight.
- It gains Reach.
- It gains Siphon.

**Moonlit Accord** — 4 | Tactic | Drift | Uncommon | Shift, Reactive
Choose one:
- Counter a Tactic.
- Prevent the next 3 damage to any target this turn.
- Target Unit gets +2/+2 until end of turn.
**Resonance [3]:** Choose two instead.

**Morrow-Skin Mystic** — 4 | Unit (Human Mage) | Drift | Rare | Shift
*3 / 3* — When this enters play, choose one:
- It gains Flight and Fade.
- Draw two cards, then discard one.
- Return a Drift Tactic from your Discard to your hand.
**Resonance [4]:** Choose two instead.

**The River Who Remembered** — 7 | Unit (Spirit, Legendary) | Drift | Mythic | Shift
*5 / 5* — Flight, Fade.
When this enters play, choose two (Resonance [5]: choose three):
- Deal 4 damage to any target.
- Return up to two Units to their owners' hands.
- You gain 6 Life.
- Return a Unit from your Discard to the battlefield.
*"There was a river. There was a shape in it. The shape was you, once. Or will be."*

### One representative signpost per new faction

**Cinder-Kin Warband** — 3 | Unit (Human Warrior) | Ember–Flock | Uncommon
*2 / 2* — Swift, Kin.
*Pair theme: wide-and-fast aggression. Token-Ember in one card.*

**Glassborn Herald** — 3 | Unit (Spirit) | Tide–Drift | Uncommon | Shift
*2 / 3* — Flight. When this enters play, choose one:
- Draw a card.
- Return a Unit to its owner's hand.
*Pair theme: reactive modality — hand in one mode, removal in the other.*

---

## 6. Open design questions

Things I haven't resolved that you'll want to decide before playtesting:

1. **Does Kin stack with itself on the same unit in the same turn?** The text as written says yes (each new entry triggers every Kin on board). That can get spiky — five Kin units entering over a turn = the last one enters with +4/+0, the first one ends with +4/+0 too. Might need to cap at "+1/+0 per turn regardless of triggers" for Flock to be fair. Needs playtesting.

2. **Shift with Resonance: what happens if a mode targets something that's no longer valid at resolution?** Standard rule is "do what you can, skip what you can't." Worth writing explicitly into the Shift rules so players aren't confused when their Moonlit Accord loses one mode on resolution.

3. **Fade interaction with reanimation.** Shade's Echo puts copies of Tactics into the Void. Drift's Fade puts Units into the Void. Currently neither counts for Resonance, which is correct, but it means Shade + Drift decks *drain their own Resonance* every time Fade triggers. This is probably a feature, not a bug — it stops the pair from being oppressive — but it should be deliberate.

4. **Whether Flock tokens should be Flock-faction or Neutral.** I wrote them as Flock above (Sparrow, Fingerling tokens). This means they count for Resonance *on resolution of the card that made them* — no, wait, tokens never enter Discard, so they never contribute. Tokens being Flock-faction only matters for *being-targeted* by faction-restricted effects ("destroy a Flock Unit"). Making them Flock is slightly stronger for the opponent's "answer cards" market; making them Neutral is slightly stronger for Flock. My instinct is Flock-faction (it's honest), but either is defensible.

5. **Whether the 7-faction design survives draft.** 7 factions × 3 packs × 15 cards = a lot of variance. Might need to go to 18-card packs or 4 packs to give drafters enough picks to commit. Or shrink deck size to 28. This is the #1 thing to playtest.

---

*End of rework.*
