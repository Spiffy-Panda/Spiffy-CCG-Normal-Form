# Encoding Design Notes

Non-code commentary on the CCGNF encoding. Carried forward from the old `Resonance-CCG-NF.md` §7.

---

## What fell out cleanly

- Every keyword is a macro in `engine/03-keyword-macros.ccgnf`.
- Every tier-table card is `Tiers([...])`.
- Every Banner-conditional is `When(Banner(F), ...)` or `If(BannerExists(), ...)`.
- Every "counts echoes" card (Bloom, Myrrhan's Sapling buff, The Endless Thicket Peak) reads the Resonance Field directly via `CountEcho(F)` or `Resonance(F, N)` — no separate mechanism.
- The play chain (`engine/08-play.ccgnf`) and Clash resolution (`engine/09-clash.ccgnf`) are fully event- and characteristic-driven. No procedures in `engine/`; the play protocol and Clash are both sequences of Triggered abilities on `Game` plus derived characteristics on `Unit` and `Arena`.
- Setup (`engine/05-setup.ccgnf`) is a single Triggered ability on Game, composed of named sub-effect macros. Mulligan is a `Choice` inside a `Repeat`, no control-flow primitive invented.
- Per-Unit Clash contribution lives on the Unit itself as two derived characteristics. A new contribution-altering keyword adds a Static to the Unit; Clash orchestration never needs to change.

## Bugs fixed

1. Removed the invented "turn-1 first player skips draw" Replacement. GameRules §4 makes second-player's draw-6 the sole compensation.
2. Debt uses `printed_cost`, not `effective_cost` (R-5).
3. Debt cap uses `next_rise_refresh(p)` (R-6), correctly indexing the player's next Rise regardless of who is first or second.
4. Removed the duplicate Rampart reset from Rise; healing lives only at end of Fall (R-4).
5. Thessa and Eremis once-per-turn tracking now uses the harness's `once_per_turn` (and `once_per_turn_per_trigger_subject`) ability annotations, not hand-rolled counters.
6. Collapse SBA now uses the proper type expression for Arena-attached Standards and emits `Event.Destroy(reason: conduit_collapse)` so Recur can intercept (R-1).
7. The Last Whisper now targets a `PlayBinding` entity (the addressable thing in the play chain) rather than a loose "pending Maneuver" reference.

## Remaining subtleties worth a playtest

1. **"When you play a card" + Reshape.** The Standing Wave's Reshape fires at `announcement`, i.e., before tier binding. A well-timed Reshape can thus retroactively change which tier a card lands on. Intended, but surprising to new players.

2. **Harborkeeper's "would enter."** Encoded as a Replacement on `Event.EnterPlay(target=u)`. Requires the harness to treat EnterPlay as a *pending* event with a mutable `arena` field that Replacements can rewrite before commit. Standard would-event plumbing; call it out in harness docs.

3. **The Unread Page + Peak flapping.** Opponent's choice is locked at Enter-Play. If Peak is reached later, a Static layers both modifiers on. If Peak is then lost, the Static stops applying its forced modifier, but the opponent's original choice is still active. If Peak returns, the Static re-applies. Check with playtest whether "once both modifiers have been forced, opponent loses the ability to revert to just one" is the intended feel.

4. **Eremis's copy semantics.** Handled cleanly by `PlayAsCopy` in `engine/08-play.ccgnf`: the copy is a fresh PlayBinding with `play_source: from_copy`, which skips cost entirely and fails `ShouldPush`. No ad-hoc "does this Push?" logic anywhere else.

## Deferred schema choices

- **PhantomReturn as an Event type.** Encoded as distinct so Blankface and Oreth can trigger cleanly. Alternative: a `MoveTo(target → Hand, reason: phantom_return)` with triggers filtering on the reason tag. Equivalent semantics but requires all move-to-hand triggers to be reason-aware.
- **Implicit DeploymentSickness on all Units.** Currently every Unit card lists `DeploymentSickness` in its keywords array. Cleaner would be to move it to a Game-level Triggered ability that fires on every `Event.EnterPlay(target.type == Unit)` and relies on Blitz's suppression to override. Either choice is sound; explicit is more lintable, implicit is less noisy.
- **Aether schedule on Player vs. Game.** Hardcoded on Player currently. Moving to a Game characteristic enables future format overrides (e.g., "fast mode": `[4,5,6,7,8,9,10]`).
- **`used_this_turn` vs `used_this_turn_per_subject`.** Two gating modes now exist in the harness vocabulary: blanket (Thessa) and per-trigger-subject (Eremis). No other card in the current set needs a third mode.
