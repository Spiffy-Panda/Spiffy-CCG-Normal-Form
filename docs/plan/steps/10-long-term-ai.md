## Step 10 — Long-term CPU AI (no look-ahead search)

Goal: a CPU that plays a passably competent game of Resonance without
minimax / MCTS / tree search. "Passable" meaning: picks on-curve
Units, doesn't waste damage, defends a collapsing Arena, closes out
when the opponent has two near-collapsed Conduits. Entirely reactive
to the current state + the current `InputRequest`.

Read first: [reference/builtins.md](../reference/builtins.md) for
LegalAction shapes, [api/rest.md](../api/rest.md) for the SSE room
channel, and commit `61afdb3` for the current fixed-ladder baseline
this step replaces.

## Why not search

Resonance has imperfect information (opponent's hand + Arsenal),
interactive responses (Interrupts / replacements when those land),
and small-but-meaningful branching at every priority window. A full
minimax wants to enumerate opponent hands, prune on aether, and
track Resonance Field state across turns. That's doable but:

- Decks of 30, hands of 6+, even a cheap opponent-hand assumption
  explodes to thousands of rollouts per decision.
- The engine isn't optimised for cloning game state (no pooled
  entities, no copy-on-write zones).
- Every new encoding mechanic (Interrupts, Phantom, Push) compounds
  the branching.

A knowledge-driven reactive AI is cheaper to build, cheaper per
decision, and — critically — easier to tune by someone who knows the
game. Search is a later tier when cards have locked down and
performance matters.

## Two well-known patterns

### Behavior Trees

*(Referenced: `..\AI-BT-Gym\simulation\BehaviorTree` in a separate
repo. Not read directly; commentary below is generic.)*

A BT is a hierarchy of composable nodes — Selector (try children
until one succeeds), Sequence (all in order), Decorator (modify a
child's result), Action (leaf that does work). It's essentially a
visual / data-driven state machine.

**Strengths.**

- Clear trace for "why did the AI do X?" — you can highlight the
  firing path. Easy to debug.
- Extending the AI is adding a branch; no re-scoring of the whole
  tree.
- Dramatic / scripted behaviour ("if opponent has lethal next turn,
  play the defensive Maneuver no matter what else is going on") maps
  naturally to a Selector guard.
- Cheap per tick.

**Weaknesses for a card game.**

- Weighing multiple valid plays is awkward. A BT that hits a
  `play_unit` leaf has to pick *which* unit inside the leaf —
  effectively a utility decision anyway.
- As the branches multiply, you end up with deeply-nested Selectors
  trying to encode "try the best option first." That inverts the
  hierarchy — you're fighting the data structure.
- Cards don't map 1:1 to behaviours. A BT shines when "the agent" has
  a fixed move set; in Resonance the move set is whatever is in
  hand. Tree shape has to re-derive from hand every turn.

BTs are a great fit for "play a card, then check win condition, else
defend, else cycle draw" phase-level logic. They're a bad fit for
"given these 6 hand cards, which one is best right now?"

### Utility AI

*(Referenced: `..\utilityAI` in a separate repo. Not read directly.)*

Each candidate action gets a score from a set of weighted
*considerations*. "How much damage can I do?" "How exposed are my
conduits?" "Does this push me closer to a Resonance tier?" The
action with the highest weighted score wins. Considerations can be
normalised (0..1) and blended via curves (linear, exponential,
sigmoid) so weights stay human-readable.

**Strengths for a card game.**

- Natural fit for "given N legal plays, pick the best one."
- Weights are tunable by someone who knows the game, not the
  programmer.
- Adding a new consideration (e.g. "prefer plays that push a BULWARK
  Echo when I'm at BULWARK 2") is a single scorer, no tree rewire.
- Multiple considerations can pull in opposite directions (aggression
  vs tempo) and the blend is transparent.

**Weaknesses.**

- "Why did it do that?" is a sum of numbers — harder to narrate than
  a BT path. Mitigation: a debug overlay that shows the top 3 scored
  actions + their consideration breakdowns (same shape as the
  "inspector" we already render for cards).
- Pure utility is flat — it doesn't cope with sequential decisions
  within a single turn ("play Spark, then pick its target, then
  attack") unless each decision is scored independently. Works fine
  for Resonance because the engine already slices decisions into
  independent `InputRequest` pendings.
- A bad weight tune can oscillate. Guardable with a "don't switch
  plans mid-phase" rule.

## Recommendation: Utility AI as the primary, BT for phase-level shells

The engine already gives us exactly the right interface — each CPU
decision is one `InputRequest` with a fixed list of `LegalAction`s.
Utility AI over that list is the right shape.

- **One scorer per `LegalAction.Kind`.** `play_card`,
  `target_entity`, `target_arena`, `declare_attacker`,
  `choice_option`, `pass_priority`.
- **Each scorer is a weighted sum of considerations.** Start with
  the obvious ones:
  - `play_card`: on-curve (cost == current aether → 1.0), tempo
    (force per aether), Resonance alignment (does the card's faction
    match our current Banner), clear-hand bonus (at hand cap −1).
  - `target_entity`: damage efficiency (pick lowest HP above zero),
    strategic priority (opponent Conduit > opponent Unit > friendly
    Unit with positive removal value), never self-target.
  - `target_arena`: Unit-overlap (prefer arena where opponent already
    has a Unit — forces Clash), conduit weakness (prefer arena where
    our conduit is healthy and theirs is soft), curve protection
    (don't over-commit to one arena).
  - `declare_attacker`: always attack unless doing so would break a
    defensive line or expose a conduit to retaliation (a shallow
    threat check, not full search).
  - `choice_option`: card-specific tables (Mulligan → pass unless
    hand has 0 cards of current Banner; generic → pass).
  - `pass_priority`: score is the opportunity cost of the
    best-available play.
- **Top-level BT sits over the utility system only for global
  intent.** States like `early_tempo`, `pushing_faction`,
  `defend_conduit`, `lethal_check`. The current state multiplies the
  weights (e.g. in `defend_conduit`, the `target_entity` removal
  consideration gets × 2.5). This is how you get "context-aware
  utility" without abandoning the transparency of either system.
- **No game-tree cloning.** Every consideration reads directly from
  the snapshotted `GameState`. If a consideration *does* want to
  simulate ("how much aether would I have next turn if I played
  this?"), simulate at the counter level, not the full engine.

## Step layout

### 10a. Scaffold `IRoomBot` + Utility skeleton

- Extract the current fixed-ladder policy in `Room.ChooseCpuAction`
  into an `IRoomBot` interface:
  ```csharp
  public interface IRoomBot {
      RtValue Choose(InterpreterRun run, InputRequest pending, int cpuEntityId);
  }
  ```
- `FixedLadderBot` — the current 61afdb3 code.
- `UtilityBot` — new. Takes a `ConsiderationSet` (list of
  `IConsideration` instances scored per `LegalAction`). Initially
  wraps the fixed ladder as a single "hard override" consideration
  so the two bots produce identical output until new considerations
  are added.
- Room construction gets a `botKind` option: `"fixed"` (default) or
  `"utility"`.

### 10b. First real considerations: play-card scoring

- `OnCurveConsideration` — linear curve: cost == aether → 1.0,
  |diff| == 1 → 0.5, |diff| ≥ 2 → 0.0.
- `TempoPerAetherConsideration` — `force / max(cost, 1)` normalised
  0..1 over a fixed upper-bound (say 4).
- `ResonanceAlignmentConsideration` — matches the played card's
  faction(s) to the player's current Banner; +0.5 for match, +0.0
  otherwise. Reads from `player.derived.banner` when wired, else
  falls back to counting Resonance Field contents directly.
- Weights exposed in a JSON file `encoding/ai/utility-weights.json`
  so non-programmers can tune without rebuilding.

### 10c. Target + arena considerations

- `LowestLiveHpConsideration` — for `target_entity`.
- `OpponentPriorityConsideration` — Conduit > Unit > everything else.
- `OverlapConsideration` — for `target_arena`, +1.0 if opponent has
  a Unit in that arena.
- `ConduitSoftnessConsideration` — lower target conduit integrity →
  higher score (close-the-game bias).

### 10d. Top-level BT shell (4 states)

- `EarlyTempo` — rounds 1–3, weight curve aggressively.
- `Pushing` — when current Banner matches 2+ cards in hand, bias
  toward Resonance plays.
- `DefendConduit` — when any of our conduits is at ≤ 3 integrity,
  bias target scorers toward removal of opponent's Units.
- `LethalCheck` — when opponent has 1 standing conduit, bias
  target_entity toward that conduit's integrity ≥ our damage output.

BT node per state; Selector picks the first whose precondition
matches; chosen state's weight table loads into the Utility bot
before scoring.

### 10e. Debug overlay + telemetry

- Reuse the web tabletop's right column. When a human-viewer room
  happens to be CPU-turn-blocked (rare — CPU moves fast), add a
  debug pane showing the top-3 scored actions and their
  consideration rows. Same pane for Godot when 9c lands.
- Log top-score and chosen action to the SSE frame as a new
  `CpuDecision` event when `CCGNF_AI_DEBUG=1`. Playbacks + the
  export endpoint already surface these.

### 10f. Strength benchmark

- Fixture-driven matches: FixedLadder vs Utility, 100 games per
  (deck, seed) pair, on a hand-curated set of 8 deck pairs covering
  faction matchups. Record win-rate and avg-turn-count. CI artifact.
- Target: Utility wins ≥ 55% mirror matches, ≥ 65% vs FixedLadder in
  favourable matchups, never < 35% in worst-case. The absolute
  numbers matter less than the regression signal when a consideration
  weight changes.

## Won't do in this step

- **Any multi-ply search.** Separate step (call it 11) once the
  engine has Interrupts + faster state cloning.
- **Deck building / deck choice.** The AI plays the deck it's
  given.
- **Opponent modelling / hand simulation.** Considerations read the
  snapshotted `GameState` only.
- **Learned weights / MLP.** Weights are hand-tuned JSON.

## Open questions

- **Where does the bot live?** Option A: inside `Ccgnf.Rest.Rooms`
  (current). Option B: extract to `src/Ccgnf.Bots/` so CLI /
  Godot / REST can all embed it. Prefer B once a second host wants
  it.
- **Determinism.** Seed-stable bot behaviour is worth keeping —
  same seed + same InputRequest history = same decisions. Don't
  introduce RNG into the scoring; if a tie-break needs randomness,
  seed it off `room.seed + pending.Prompt.Length`.
- **Playtest feedback loop.** Once humans play against Utility,
  collect complaints about "the AI did a weird thing" as
  consideration-weight test cases. That's the right tuning signal,
  not self-play Elo.
