# Engine completion guide — how to finish the v1 interpreter

*Written 2026-04-20 at the end of the step-12 balance arc, once it became
clear most card text is currently cosmetic because the interpreter doesn't
dispatch the ability kinds the cards declare. This is the instruction doc
for the next session picking up that work. Read end-to-end once before
touching code.*

**Start here → §"The first concrete task: wire Fortify + Sentinel".**
The rest of the document teaches you how to decide what to do next.

---

## 1. Current state of the engine — what's wired vs not

The Resonance encoding under `encoding/engine/*.ccgnf` and `encoding/cards/`
declares a mature game with ~15 keywords, layered static abilities,
triggered abilities on any entity, and replacement abilities. The v1
interpreter under `src/Ccgnf/Interpreter/` ships roughly a tenth of
that surface.

### Wired (verified working end-to-end)

| Concern | Where | Evidence |
|---------|-------|----------|
| Parser + preprocessor | `src/Ccgnf/Ast/`, `src/Ccgnf/Preprocessor/` | `ParserTests.cs`, `PreprocessorTests.cs` |
| Project loading + validation | `src/Ccgnf/Interpreter/ProjectLoader.cs` | `EncodingCorpusTests.cs` |
| Setup + event loop (Rise → Channel → Clash → Fall → Pass) | `Interpreter.cs` + the `Game.abilities += Triggered(...)` handlers in `encoding/engine/06-turn.ccgnf` | game runs to cap |
| Aether refill + Debt zeroing at Rise | `encoding/engine/06-turn.ccgnf:15-20` + `RefillAether` builtin | phase tests |
| Draw at Rise, deck-out → Lose | `06-turn.ccgnf:28-30` + `Draw` builtin | `ConduitCollapseTests.cs` |
| Unit play (hand → battlefield, arena pick, cost paid) | `Builtins.cs` `PlayUnit` | `ClashTests.UnitPlay_*` |
| **Maneuver `OnResolve`** | `Builtins.cs:558` `GetCardOnResolve` path | EMBER burn cards deal damage in bench |
| Atomic `DealDamage` (`current_ramparts → current_hp → integrity`) | `Builtins.cs:433-438` | `ClashTests` |
| Clash attack/hold prompt per Unit, raw Force → Conduit | `Builtins.cs:740-847` `ResolveClashPhase` | `ClashTests.Clash_Attack_*` |
| Conduit collapse SBA (integrity → 0) | `Interpreter.cs:296-340` `RunSbaPass` | `ConduitCollapseTests.cs` |
| Two-conduits-lost victory SBA | same | same |
| Counter / characteristic / flag mutation builtins | `Builtins.cs` `SetCounter`, `IncCounter`, `SetCharacteristic`, `SetFlag` | spot tests |
| `Sequence`, `If`, `ForEach`, `Choice`, `Target`, `NoOp` control flow | `Builtins.cs` evaluator branches | parser + resolve tests |

### Declared but NOT dispatched (critical gap)

The deal-breaker: **`Interpreter.DispatchEvent` at `src/Ccgnf/Interpreter/Interpreter.cs:275-293` only iterates `game.Abilities`**.
Every `Triggered(...)`, `Static(...)`, and `Replacement(...)` ability
attached to a Card, Unit, Conduit, or Arena is parsed into the AST,
stored on the entity, and **never evaluated**. That's why the bench
numbers across step 12 showed closer cards not moving the needle the
way their text suggested: the text resolves at the CCGNF layer, the
interpreter never sees it.

Specifically missing:

| Ability kind | Where it matters |
|--------------|------------------|
| `Triggered` on non-Game entities | `OnEnter`, `OnArenaEnter`, `OnCardPlayed`, `EndOfClash`, `StartOfYourTurn`, `EndOfYourTurn`, `Event.PhantomReturn`, `Event.Destroy`, etc. Every keyword macro that expands to a triggered ability. |
| `Static` continuous abilities | Layered modifiers — "while X, this has +Y Force" / "while controller's Conduit ≥ 4, this has +Fortify". Every `Static(modifies: …, check_at: continuously, rule: …)`. |
| `Replacement` abilities | `Recur` (move to Arsenal instead of Cache), `Unique` (dup goes to Cache), `Shroud` (target legality), `Harborkeeper`'s redirect-to-adjacent. |

### Keyword-level consequences

Given no non-Game triggered / static / replacement dispatch, these
keywords are all currently **declared cosmetics**:

| Keyword | Declared in | Actually fires? |
|---------|-------------|-----------------|
| **Sentinel** | `03-keyword-macros.ccgnf:61-64` | **no** — Static on Unit; never evaluated. Unit projects full Force into Clash instead of 0. |
| **Fortify N** | `03-keyword-macros.ccgnf:43-48` | **no** — Static on Unit; Ramparts never get the +N bonus. |
| **Mend N** | `03-keyword-macros.ccgnf:52-57` | **no** — Triggered(OnEnter) on Unit; never fires. |
| **Blitz** | `03-keyword-macros.ccgnf:23-26` | **no** — Static modifying DeploymentSickness flag. Units still behave as if Deployment-Sick rules don't exist anyway, so this is a double no-op. |
| **Surge** | `03-keyword-macros.ccgnf:13-20` | **no** — Static on cost_computation. Cost is read from `cost:` field directly. |
| **Ignite N** | `03-keyword-macros.ccgnf:30-35` | **no** — Triggered(StartOfYourTurn); never fires. |
| **Phantom** | `03-keyword-macros.ccgnf:145-158` | **no** — Triggered(StartOfClash) + ScheduleAt. |
| **Shroud** | `03-keyword-macros.ccgnf:162-168` | **no** — Static on TargetLegality. Opposing effects target shrouded Units freely. |
| **Pilfer N** | `03-keyword-macros.ccgnf:172-182` | **partial** — the `Pilfer(X)` macro expands to a Sequence of `RevealHand` + `Target + ForEach + MoveTo`. If invoked from a Maneuver's `OnResolve`, the sequence runs because OnResolve fires; but `RevealHand` is probably a no-op stub — **verify**. |
| **Drift** | `03-keyword-macros.ccgnf:73-78` | **no** — Triggered(EndOfYourTurn). |
| **Recur** | `03-keyword-macros.ccgnf:83-86` | **no** — Replacement. |
| **Rally** | `03-keyword-macros.ccgnf:106-109` | **no** — Triggered(OnArenaEnter). |
| **Sprawl N** | `03-keyword-macros.ccgnf:114-120` | **partial** — `Sprawl(X)` macro is a `ForEach + CreateToken`. Invoked from a Maneuver's OnResolve, it works. Invoked from a Unit-keyword trigger (like Vinegrowth OnEnter), the trigger never fires. |
| **Kindle** | `03-keyword-macros.ccgnf:124-135` | **no** — Triggered + Static. |
| **Reshape(n)** | `03-keyword-macros.ccgnf:90-99` | **partial** — called directly from Maneuver OnResolve → fires the Repeat + Choice chain; but the `SwapEchoPosition` builtin inside may be a stub. **Verify**. |
| `DealDamage` (Conduit-damage Maneuvers like Spark, Smolder) | — | **yes** — targets Conduit/Unit directly via OnResolve. |
| `Heal` / `Mend` (as direct Maneuver, e.g., SealTheBreach) | — | **verify** — depends on whether the `Heal` builtin actually mutates integrity upward. |

*The "partial" and "verify" rows are where you should spend five
minutes each during the audit pass (§3) before sinking a day into
wiring.*

---

## 2. The first concrete task: wire Fortify + Sentinel

Fortify + Sentinel are the two keywords that, together, define
BULWARK's entire identity and also drive the three most persistent
stall cells in the PairCorrectly bench. Wiring them is the smallest
change that produces a measurable difference on the existing benches.
It's also a complete slice of the larger problem: you touch Static
ability dispatch, keyword-macro expansion at ability-instantiation
time, and the Clash damage math.

### 2.1 What "wired" means, precisely

At end of this task the following must be true:

- A Unit declared with `keywords: [ Sentinel, DeploymentSickness ]`
  contributes `0` to `projected_force` and `(self.force + self.current_ramparts)`
  to `fortification` during Clash on its owner's controlled Arena side.
- A Unit declared with `keywords: [ Fortify(2), DeploymentSickness ]`
  has `current_ramparts = max_ramparts + 2` while the controller's
  Conduit in its Arena has `integrity ≥ 4`; loses the bonus (but is
  not destroyed) when that Conduit drops below 4.
- The per-Arena `incoming[side] = Max(0, projected_force[other] - fortification[side])`
  formula from `encoding/engine/09-clash.ccgnf:46-51` is what
  determines how much the Conduit takes at Clash — **not** the
  per-attacker raw-Force loop in `Builtins.cs:820-838`.
- `ClashTests.Clash_Attack_DealsForceDamageToOpponentSameArenaConduit`
  is updated to reflect the formula, and a new test covers a
  Sentinel absorbing Force into Fortification.

### 2.2 Three sub-steps, each shippable on its own

**Sub-step A — Static-ability evaluation on non-Game entities.**
Extend `Interpreter.DispatchEvent` (or add a sibling `ApplyStatics`
pass) so that before Clash resolves, every Unit on the battlefield
whose abilities include a `Static(modifies: …, rule: …)` has its
rule evaluated and its contribution recorded. Start with a narrow
interface: don't try to implement the full layer system. Just expose
two new counters per Unit, `clash_projected_force` and
`clash_fortification`, defaulting to `self.force` and
`self.current_ramparts` respectively, and let Sentinel's Static rule
overwrite them. The CCGNF source for those defaults is already
there at `encoding/engine/09-clash.ccgnf:14-31`.

Test: a Unit with no keywords produces
`clash_projected_force == force && clash_fortification == current_ramparts`.
A Unit with `[Sentinel]` produces
`clash_projected_force == 0 && clash_fortification == force + current_ramparts`.

**Sub-step B — Fortify N as a Static on Ramparts.**
The Fortify keyword macro adds `+N` to `current_ramparts` while the
controller's Conduit integrity ≥ 4. This is a second Static-ability
consumer that needs the same dispatch pass from Sub-step A. The
bonus is a runtime re-evaluation, not a persistent counter mutation
— do not mutate `max_ramparts` or permanent `current_ramparts` on
the Unit. Track the bonus as a transient read through a
`EffectiveRamparts(unit)` helper the Clash code calls.

Test: a `Fortify(2)` Unit on an Arena whose owner's Conduit has
integrity 7 has effective Ramparts `base + 2`. After damage drops
the Conduit to integrity 3, the bonus disappears and effective
Ramparts drops by 2 next Clash. If the Conduit heals back to 4, the
bonus returns.

**Sub-step C — Replace the per-attacker Force loop with the
formula.** In `Builtins.cs` `ResolveClashPhase`, replace the loop
that applies `force` per attacker directly to Conduit integrity with
a per-Arena calculation:

```csharp
int projectedForce = attackers.Sum(a => GetClashProjectedForce(a));
int fortification  = GetArenaFortification(state, arenaPos, defendingSide);
int incoming       = Math.Max(0, projectedForce - fortification);
conduit.Counters["integrity"] = Math.Max(0, before - incoming);
```

where `GetClashProjectedForce` + `GetArenaFortification` consult the
values Sub-step A populated. Emit a single `DamageDealt` event per
Arena-side rather than one per attacker (easier to reason about
later for card triggers like "when a Conduit takes damage …").

Test: update
`ClashTests.Clash_Attack_DealsForceDamageToOpponentSameArenaConduit`
(currently asserts `integrity == 5` after a Force-2 Warrior attacks
a 7-integrity Conduit → unchanged because there's no Fortification
in that fixture). Add a new fixture and test where defender has a
Sentinel Unit with Force 3 Ramparts 2; attacker's Force 4 → Clash
produces `incoming = max(0, 4 - (3+2)) = 0`, Conduit still at 7.

### 2.3 Expected bench movement after Fortify + Sentinel land

Re-run `PairCorrectly` and `AiDeckMatrix` at baseline seed 1.
Predictions:

- BulFort cells stiffen — BULWARK is actually a wall now. BulFort-vs-
  TiThWave was `3/37/0` (BulFort wins 3, TiThWave wins 37); expect
  BulFort decisive wins flat or down, TiThWave wins down, draws up.
  The stall cells (BulFort-vs-EmbHell `2/1/37`) likely stiffen
  further — EMBER's Force-based pressure is now absorbed properly.
- EmbHell shifts toward direct-damage plays; the `DealDamage`
  Maneuvers bypass Fortification, so EMBER's burn suite becomes
  load-bearing. Cells previously decided by attacking Units flatten.
- HolReap cells basically unchanged — HOLLOW's relevant keywords
  (Phantom, Shroud) aren't wired yet, so the deck is still hollow
  of board-answers.

**If the BulFort-vs-EmbHell 37 draw cell moves >5 pp in either
direction, Fortify + Sentinel are landing.** If it stays at 37/40,
check whether BULWARK Units are actually being played or whether
fortress is choosing `hold` (the Clash attack/hold prompt)
irrespective of keywords.

### 2.4 Commit shape

Three commits, one per sub-step, each with its own test. A fourth
commit re-runs both benches and updates
`docs/plan/balance/card-pool-2026-04-20-final.md` (or creates a new
dated sibling) with the delta.

---

## 3. How to figure out what needs to be done after Fortify

Do not try to wire every missing keyword at once. The v1 interpreter
was pragmatic — what got wired was what cards actually needed the
first time they were exercised by a bench. Follow the same pattern:
pick the keyword whose absence is most visible in benches, wire it,
bench again, repeat.

### 3.1 Per-keyword probe method

For each keyword you're unsure about, write a three-line test that
proves (or disproves) it fires end-to-end. Template, adapted from
`ClashTests.cs`:

```csharp
[Fact]
public void KEYWORD_DoesSomethingObservable()
{
    var file = LoadFixture();  // or LoadEncoding() for the real set
    using var run = NewInterpreter().StartRun(file, WithDeck("CardThatUsesKeyword"));

    // Drive the interpreter to the point where the keyword would fire.
    run.Submit(/* play the card */);
    run.Submit(/* advance phases */);

    // Assert the observable side-effect. For Mend: Conduit integrity.
    // For Phantom: a hand-size change at End of Clash. For Sprawl on
    // an OnEnter (not direct Maneuver call): a ThornSapling entity exists.
    Assert.Equal(EXPECTED, run.State.SomeCounter);
}
```

If the assertion fails and the side-effect is a no-op, the keyword
is **not wired**. If it passes, the keyword is wired (though layered
interactions may still be broken — write a second probe for those
when you get to them).

A good place to accumulate these is a new file
`tests/Ccgnf.Tests/KeywordWiringTests.cs` so the question "is X
wired?" has a single answer location.

### 3.2 Priority order for the audit

Rank keywords by combined (a) impact on bench × (b) cost to wire.
Rough cost proxy: triggered-on-Unit is the biggest infrastructure
unlock (unblocks dozens of cards), so wiring it even once pays for
itself.

1. **Static dispatch (done in §2 for Sentinel + Fortify).**
   Unlocks: every `Static(modifies: Characteristic(self, *))` on
   non-Game entities. Roaring Champion's +1/+0 aura, The Quiet
   Wall's Sentinel grant, Vaen's +Ramparts aura, Thicketwarden's
   conditional +Force, Thornlord's aura, ShadowExecutor's
   conditional Blitz. Huge cascade.
2. **Triggered-on-Unit dispatch.** Unlocks every `OnEnter`,
   `OnArenaEnter`, `OnCardPlayed`, `EndOfClash`, `StartOfYourTurn`,
   `EndOfYourTurn`, `Event.PhantomReturn` trigger. This is the
   single biggest unlock by card count — essentially all 19
   closer cards authored in step 12.3 plus most of the pre-12.3
   pool. **Do this right after Fortify.**
3. **Replacement-ability dispatch.** Unlocks Recur, Unique, Shroud
   target-legality, Harborkeeper's redirect. Harder than Triggered
   because Replacement interposes on an event before it commits;
   requires a "replace_with" evaluation path and a guard predicate
   check.
4. **Phantom's schedule-at-end-of-clash dance.** `ScheduleAt` is
   effectively a deferred Triggered fire; once Triggered-on-Unit
   works, Phantom is mostly a matter of plumbing the `Phantoming`
   state flag into Clash contribution.
5. **Surge cost reduction.** Fiddly because the cost is read at the
   play-protocol step, which itself may not fully exist yet.
   Low priority until the play protocol's cost resolution is
   generalised beyond the `cost:` field lookup.
6. **Ignite, Drift, Rally, Sprawl keyword form (vs Maneuver-level
   Sprawl).** All triggered-on-Unit; fall out of (2).
7. **Reshape echo swap + Resonance field manipulation.** The
   Resonance Field + Banner + Echo mechanics are a whole
   sub-system. Check whether the ResonanceField zone is being
   populated at all (`StateBuilder.cs`); if not, this is a
   separate wiring exercise.

### 3.3 Systematic sweep to identify everything else

Once the high-impact items are wired, sweep the encoding for any
remaining ungated stubs:

```bash
# Every ability attached to a non-Game entity via +=
grep -rn "\.abilities += " encoding/cards/ encoding/engine/ \
  | grep -v "^encoding/engine/0[4-6]\|^Game\.abilities"

# Every Static(…) that isn't on Game
grep -rnE "Static\(" encoding/cards/ encoding/engine/

# Every Replacement(…)
grep -rnE "Replacement\(" encoding/cards/ encoding/engine/

# Every Triggered(…) — the ones that aren't shorthand-wrapped
grep -rnE "Triggered\(" encoding/cards/ encoding/engine/ \
  | grep -v "define On\|define Start\|define End"
```

For each hit: does the interpreter have a dispatcher that picks it
up? If not, add a wiring test as per §3.1. Expect ~20-30 individual
gaps.

### 3.4 Non-keyword gaps to keep in mind

These came up incidentally during the audit and should be checked
when the keyword pass is done:

- **Debt accrual from Interrupts.** `engine/06-turn.ccgnf:20` reads
  `p.debt` but nothing in `Builtins.cs` writes to it. Interrupt's
  "cost doubles when played on opponent's turn" rule won't bite
  until this lands.
- **Banner derivative.** `design/GameRules.md` describes a Banner
  that follows your most-pushed faction. The Evaluator likely
  computes `Banner(F)` as "always false" — confirm.
- **Resonance Field / Peak.** The 5-Echo FIFO that all tier
  predicates key off. `CountEcho(F)` needs to actually count — if
  `ResonanceField` is empty at runtime, `Peak(F)` is always false
  and every "Peak BULWARK" / "Peak TIDE" effect is dead.
- **Ramparts heal at End of Fall.** `engine/06-turn.ccgnf:66-67`
  describes `SetCounter(u, current_ramparts, max_ramparts_of(u))`
  on each of the controller's Units — verify `max_ramparts_of`
  exists and this ForEach actually fires (it's a Game ability, so
  it *should* dispatch).
- **`RevealHand` / `MoveTo(exiled)`.** Pilfer depends on both.
  Reveal is probably a no-op visualiser; exile depends on
  zone-move plumbing.

---

## 4. Completion criteria for "the engine is done"

Mark the engine complete when all of these are true. Until then,
benches are measuring a subset of the game.

- [ ] Every keyword in `03-keyword-macros.ccgnf` has a passing
      wiring test in `tests/Ccgnf.Tests/KeywordWiringTests.cs`.
- [ ] Triggered dispatch walks all entity abilities, not just
      `Game.Abilities`.
- [ ] Static dispatch evaluates modifiers continuously (at least at
      start-of-Clash and on counter change) and applies them as
      layered characteristics, matching `common/01-schema.ccgnf`
      layer 2/3 semantics.
- [ ] Replacement dispatch walks matching events before the main
      effect commits, and at least one Replacement-using card
      (Recur on any TIDE Unit, or `Harborkeeper`) has an integration
      test proving the replacement fires.
- [ ] `ResonanceField` is actually populated by `PushEcho` and
      readable by `CountEcho` / `Resonance` / `Peak`.
- [ ] A re-run of `PairCorrectly.tournament.json` at baseline seed
      1 returns a materially different number from the current
      `ai-testing-data/post-knob3.PairCorrectly.results.json`. It
      doesn't need to be lower (in fact BULWARK walls may stiffen
      draws initially); it needs to be **different** in a way you
      can explain card-by-card.

That last bullet is the honest integration check. If you wire the
keywords and PairCorrectly doesn't move, either the wiring is
wrong, or the bots aren't using the newly-live abilities in their
scoring. Both diagnoses are valuable.

---

## 5. Where to track progress

- **One commit per sub-step.** The three-step Fortify pass is three
  commits, not one. Smaller commits let you revert one sub-step if
  it regresses benches or tests.
- **Update the step-12 index** at
  `docs/plan/steps/12.INDEX-2026-04-20.md` whenever a keyword goes
  from "unwired" to "wired" — the arc's bench numbers will shift
  and the index is the single source of truth for "where are we
  right now".
- **Consider opening a sibling step file** at
  `docs/plan/steps/13-engine-completion.md` with scope-in / scope-
  out / exit criteria mirroring the §4 bullets. The step-12 balance
  arc closes out on that file; 13 is about the engine catching up.
- **Bench diff convention.** Every interpreter change that could
  plausibly move the bench lands with a paired
  `ai-testing-data/post-wiring-<thing>.PairCorrectly.results.json`
  so the next session can diff it trivially.

---

## 6. What not to do

- **Don't touch cards or decks to compensate for unwired keywords.**
  Every card / deck change made to force a bench movement before
  keywords are wired is debt that has to be re-tried once the
  mechanics are live. The 12.3 card authoring pass already accepted
  this; don't extend the debt.
- **Don't ship more engine knobs (Knob 1 / 2 / 4 from the 12.2
  audit) until keywords are wired.** They're acting on a damage
  model that doesn't include Fortification. The tuning space you'd
  be exploring is cosmetic.
- **Don't delete the step-12 bench artifacts.** They're the
  pre-wiring baseline the post-wiring world will be compared to.
- **Don't rewrite the card-cluster skill or re-author cards** until
  Triggered-on-Unit dispatch works. The skill is fine; cards are
  fine; the interpreter is what's behind.
