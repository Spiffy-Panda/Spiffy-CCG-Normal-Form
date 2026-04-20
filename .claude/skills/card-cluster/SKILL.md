---
name: card-cluster
description: Generate a cluster of Resonance cards following the standard set distribution, validate through the CCGNF toolchain, iterate with the user, and commit approved cards to encoding/cards/. Invoke when the user asks to "generate cards", "make a card cluster", "design new cards", or otherwise requests card creation for Resonance.
---

# Card Cluster Generator

Creates a self-consistent cluster of cards for **Resonance** (the reference CCG in this repo), validated through the CCGNF pipeline, presented to the user for approval, and finally committed to the long-term encoding under `encoding/cards/`.

## Core contract

- **Structural choices come from a seeded PRNG, not the LLM.** Rarity, faction, card type, keyword, and **role** selection are `python3 random.choices()` calls, not "what feels right." The seed is recorded in the working file header and a re-roll uses `seed + 1`.
- **Narrative content comes from the LLM.** Card names, flavor text, rules prose — creativity is fine for these.
- **Every card carries two representations in the working file** — a human-readable block comment styled like `design/Supplement.md`, followed by the CCGNF `Card NAME { ... }` declaration. These MUST stay in sync after every edit.
- **Every card carries a role tag** — `closer | setup | disruption | filler` — in its block comment, and role selection is PRNG-weighted by the gap surfaced by the latest `docs/plan/balance/card-pool-<date>.md` audit note. Cards never ship with the role tag missing; the Sync check fails if the tag disagrees with the ability body.
- **Nothing ships without a clean toolchain pass.** Parser-clean is the minimum; when the validator exists, validator-clean is required.

## Inputs to parse from the user prompt

| Parameter         | Default                               | Notes                                             |
|-------------------|---------------------------------------|---------------------------------------------------|
| `N` (size)        | 10                                    | Total cards in the cluster                        |
| Faction filter    | none (all factions fair game)         | e.g., "5 EMBER cards"                             |
| Rarity filter     | none (use standard distribution)      | e.g., "only commons"                              |
| Theme             | none                                  | Narrative shape: "removal", "go-wide", "attrition" |
| Seed              | `date +%s` at invocation              | Use `seed + 1` on re-roll                         |

Record all inputs in the working file header.

## Workflow

### 1. Ensure the toolchain is built (Release)

```bash
make build CONFIG=Release
```

Fall back to `dotnet build Ccgnf.sln -c Release` if `make` is unavailable (common on Windows/bash where `make` may not be installed). The `dotnet` fallback is a fully supported path — don't ask the user to install `make`. If the build itself fails, report the error and stop — do not proceed with a broken toolchain.

### 2. Read current distribution and derive rarity weights

Load `encoding/cards/DISTRIBUTION.md`. Note the current counts per faction × rarity and the deficit vs. the §4 target distribution from `design/Supplement.md`:

- Common: 44%, Uncommon: 32%, Rare: 18%, Mythic: 6%

**Don't pick weights by eye.** Run the helper:

```bash
python3 tools/cluster-rarity-weights.py
```

It reads the current Totals table from `DISTRIBUTION.md` and prints `random.choices`-ready weights using `weight = target × clamp(target/current, 0.5, 2.0)` — over-represented rarities get downweighted, under-represented ones get upweighted, with the clamp preventing extreme swings on small sets. Use the printed weights verbatim in step 3's rarity roll (unless the user gave a rarity filter).

If a user-specified theme implies a non-standard distribution (e.g., "give me a mythic cycle"), prefer the user's intent over the helper's tilt.

### 3. PRNG-driven allocation

All four of these use `python3 -c` with `random.seed(int(seed))`. Invoke each as a separate shell call so the seed stream is deterministic.

**Rarity split** (unless user overrode) — use the deficit-tilted weights from step 2, not the raw target ratios:

```bash
python3 -c "
import random, sys
random.seed(int(sys.argv[1]))
n = int(sys.argv[2])
# Weights from: python3 tools/cluster-rarity-weights.py
print(','.join(random.choices(
    ['C', 'U', 'R', 'M'],
    weights=[<C>, <U>, <R>, <M>],
    k=n)))
" <seed> <N>
```

**Faction per slot** (adjust weights against current over-representation):

```bash
python3 -c "
import random, sys
random.seed(int(sys.argv[1]) + 100)
n = int(sys.argv[2])
# Base weights: mono-factions ~18% each, dual 5%, neutral 15%.
print(','.join(random.choices(
    ['EMBER', 'BULWARK', 'TIDE', 'THORN', 'HOLLOW', 'DUAL', 'NEUTRAL'],
    weights=[18, 18, 18, 18, 18, 5, 15],
    k=n)))
" <seed> <N>
```

**Card type per slot**, per-faction-tuned weights:

| Faction  | Unit | Maneuver | Standard |
|----------|------|----------|----------|
| EMBER    | 60%  | 35%      | 5%       |
| BULWARK  | 50%  | 30%      | 20%      |
| TIDE     | 45%  | 45%      | 10%      |
| THORN    | 55%  | 25%      | 20% (Kindle Standards) |
| HOLLOW   | 50%  | 40%      | 10%      |
| NEUTRAL  | 60%  | 35%      | 5%       |
| DUAL     | 80%  | 15%      | 5%       |

**Keyword selection per faction**: PRNG-pick from the signature keyword set in `design/Supplement.md §2.*`. Avoid re-using the same keyword/mechanic for multiple cards in one cluster unless the user asks for a theme.

Per-faction signature pools:

| Faction  | Signature keywords                        |
|----------|-------------------------------------------|
| EMBER    | Surge, Blitz, Ignite                      |
| BULWARK  | Fortify, Mend, Sentinel                   |
| TIDE     | Drift, Recur, Reshape                     |
| THORN    | Rally, Sprawl, Kindle                     |
| HOLLOW   | Phantom, Shroud, Pilfer                   |
| NEUTRAL  | (none — Neutral cards are glue, no signature keyword) |

**DUAL slot handling**: a DUAL slot needs *two* PRNG decisions, in this order:

1. **Pick a pair** from the launch-set archetype list in `design/Supplement.md §6.3`:
   `EMBER/THORN`, `BULWARK/TIDE`, `HOLLOW/TIDE`, `THORN/BULWARK`, `EMBER/HOLLOW`.
2. **Pick a keyword** from the *union* of both component factions' pools (or pick "no keyword" — many dual cards lean on combined factions and a small effect rather than a marquee keyword).

Sketch:

```python
random.seed(seed + 300)
for slot in slots:
    if slot.faction == 'DUAL':
        pair = random.choice(DUAL_PAIRS)        # ('EMBER','THORN'), etc.
        pool = pools[pair[0]] + pools[pair[1]]
        slot.keyword = random.choice(pool + [None])
    elif slot.faction == 'NEUTRAL':
        slot.keyword = None                     # glue, no signature
    else:
        slot.keyword = random.choice(pools[slot.faction])
```

**Keyword/type compatibility**: a few keywords are type-locked. If the PRNG roll lands on an incompatible pair, treat the keyword as a *hint* and substitute a faction-flavored effect — do not force the keyword in.

| Keyword   | Only legal on | If rolled on something else                          |
|-----------|---------------|------------------------------------------------------|
| Kindle    | Standard      | Substitute a Sprawl-/Rally-flavored effect (THORN).  |
| Interrupt | Maneuver      | Substitute a non-Interrupt Maneuver effect.          |

(See `design/Supplement.md §2.4` and `§2.6` for the canonical lists.)

**Role per slot** (runs *after* faction and type are picked, *before* keyword-driven ability authoring in step 4):

Read the target closer % per faction from the latest audit note
`docs/plan/balance/card-pool-<date>.md`. For each slot, compute
`deficit = max(0, target_closer_pct − current_closer_pct)` for that slot's
faction. Then:

- `closer_weight    = 1 + 5 * deficit`   (bigger gap → more closer slots)
- `setup_weight     = 1`
- `disruption_weight = 1`
- `filler_weight    = 0.3`              (filler is a fallback, not a target)

Normalize and `random.choices`. Example sketch:

```python
random.seed(seed + 400)
for slot in slots:
    d = max(0, faction_target[slot.faction] - faction_current[slot.faction])
    weights = [1 + 5*d, 1, 1, 0.3]   # closer, setup, disruption, filler
    slot.role = random.choices(['closer', 'setup', 'disruption', 'filler'],
                               weights=weights)[0]
```

Seed offset `+400` keeps the role stream independent of the earlier
rarity / faction / type / keyword rolls (which use `+0/+100/+200/+300`).

**Gap-aware cluster** (default mode): when the user runs `/card-cluster`
with *no* filter, run the role roll in *gap-first* mode — override the
weights above to bias toward `closer` for the faction(s) with the
largest deficit. The user can still request `/card-cluster 10 HOLLOW
setups` or similar; explicit filters always override the default.

Offsetting seed between calls (`seed`, `seed+100`, `seed+200`, `seed+300`, `seed+400`) keeps the streams independent.

### 4. Generate each card

For each slot, assemble:

- **Name**: thematic, from a PRNG-picked seed word expanded by the LLM.
- **Cost and stats**: derive from rarity and type per `design/Supplement.md §5` power-budget. Cost typically [1..6] for C/U, [3..7] for R/M.
- **Keywords**: the PRNG pick from step 3.
- **Role**: the PRNG pick from step 3's role-per-slot roll (`closer` / `setup` / `disruption` / `filler`). The role drives the ability-text family.
- **Ability text**: LLM-authored, matching the keyword behavior defined in `design/GameRules.md §11` and `encoding/engine/03-keyword-macros.ccgnf`, **and** staying inside the role's ability-template family below.
- **Flavor text**: one-liner, LLM-authored.
- **Check for name collision**: grep the existing `encoding/cards/*.ccgnf` for the proposed name. If taken, re-roll the name (PRNG `seed + offset`).

**Per-role ability templates** — pick one pattern from the slot's role
family rather than free-styling; the role tag on the card must be a
truthful description of what the ability actually does.

| Role         | Ability patterns                                                                                                                                                                                                       |
|--------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `closer`     | Direct-Conduit-damage Maneuver; Force-3+ Unit with a trigger that reaches the opposing Conduit (OnEnter, EndOfClash, EndOfYourTurn); Standard that deals 1+ damage per turn to a Conduit; faction-pattern payoff where damage scales off a faction-native resource (Sentinel count, Phantom returns, Cache size, Sapling count). |
| `setup`      | Card draw; Aether acceleration; tutor; Banner builder; Sprawl / Rally / Sapling generator; token creator; Resonance-field manipulation.                                                                                |
| `disruption` | Removal; bounce; counter; Pilfer / hand-attack; Mend / heal; Prevent; Fortify grant to self side; Sentinel grant.                                                                                                     |
| `filler`     | Vanilla body; narrow-effect support. Strongly discouraged by the role-weight (0.3); only author filler if the PRNG forces it *and* the slot genuinely has nothing better. Filler is the "we needed a 1-cost body" slot, not the default. |

A `closer` tag with no Conduit-damage line (direct or conditional via
trigger) is a sync-check failure in step 7.

**Sanity check the rarity × type combo before authoring.** Some combinations are legal-but-unusual; if the PRNG lands on one, pause to confirm the design space is real:

- **Common + Standard** is rare in the existing set (most Standards are U/R). Either keep it intentionally simple (one short ongoing effect, like "your Units in this Arena have Fortify 1") or downgrade the type to Unit/Maneuver for that slot.
- **Mythic + Maneuver** with no Unit body — possible but should feel game-defining at Peak.
- **Common + Unique** — disallowed; Uniques are uncommon-and-up by convention. Re-roll type if this happens.

### 5. Write the working file

Path: `encoding-artifacts/working-<UTC-timestamp>.ccgnf` where timestamp is `date -u +%Y%m%dT%H%M%S`.

**The block comment is the spec, not a scratchpad.** Write the final form on the first pass — no draft notes, no parentheticals like "actually X" or "TODO: rename", no in-comment self-corrections. The block comment is what's read by humans first and copied into `design/Supplement.md`-style references later. Sloppy comments cause sync-check failures in step 7 that you then have to chase.

Layout:

```ccgnf
// =============================================================================
// Cluster working file
// Generated: <iso8601-utc>
// Seed:      <seed>
// N:         <N>
// Factions:  <allocated distribution>
// Rarities:  <allocated distribution>
// =============================================================================

/*
 * Cinderling [C] (EMBER, Unit) — cost 1 — 2/1
 * Blitz.
 *
 * A 1-drop that pressures the Conduit immediately.
 */
Card Cinderling {
  factions: {EMBER}, type: Unit, cost: 1, force: 2, ramparts: 1, rarity: C
  keywords: [ Blitz, DeploymentSickness ]
  abilities: []
  // text: Blitz.
}

/*
 * <next card description> ...
 */
Card NextOne { ... }
```

### 6. Validate through the toolchain

```bash
dotnet run --project src/Ccgnf.Cli --no-build -c Release -- \
  --log-level error encoding-artifacts/working-<ts>.ccgnf
```

Iterate on errors up to 10 times:
- Read the diagnostic.
- Fix the CCGNF in the working file.
- If the fix changes semantics, update the block comment to match.
- Re-validate.

If still broken after 10 iterations, stop and surface the errors to the user with context. Do not present a broken cluster.

(When the validator lands, extend this step to run it too and iterate on its diagnostics as well.)

### 7. Sync check

Walk each card and verify the block comment and CCGNF agree:

- Stats: `cost N — F/R` in the comment matches `cost: N, force: F, ramparts: R`.
- Rarity: `[C/U/R/M]` matches `rarity: C|U|R|M`.
- Type and faction: `(EMBER, Unit)` matches `factions: {EMBER}, type: Unit`.
- Role: the `closer | setup | disruption | filler` tag in the block comment matches what the ability actually does. A `closer` tag with no Conduit-damage line (direct or via trigger that reaches a Conduit), a `setup` tag on a removal card, or a `filler` tag on a card with a real trigger are all sync failures.
- Rules text: the comment describes what the CCGNF does.

Any mismatch → fix the appropriate side before presenting.

### 8. Present to the user

Render the cards in chat in `design/Supplement.md` style, grouped by faction then rarity. Include a summary header:

```
Cluster of 10 cards (seed 1714572600):
- 5 EMBER, 2 BULWARK, 2 TIDE, 1 NEUTRAL
- 4 C, 4 U, 1 R, 1 M
Working file: encoding-artifacts/working-20260417T210000.ccgnf
```

Then each card as:

```
**Cinderling** [C] *(EMBER, Unit, closer) — 1 — 2/1*
Blitz.
*A 1-drop that pressures the Conduit immediately.*
```

Role tag renders alongside faction and type so the reader sees at a
glance how each card slots into the closer / setup / disruption mix.

### 9. Handle feedback

When the user asks for changes:

- Update both representations in the working file atomically.
- Re-validate (step 6).
- Re-sync-check (step 7).
- Re-present (step 8).

Never present a cluster that failed validation or is out of sync.

### 10. On approval: finalize

When the user says "looks good" / "ship it" / equivalent:

1. **Append each card to the appropriate long-term file:**
   - Mono-faction → `encoding/cards/<faction-lowercase>.ccgnf`
   - Dual-faction → `encoding/cards/dual.ccgnf`
   - Neutral → `encoding/cards/neutral.ccgnf`
   - Append at the end of the file. Preserve both block comment and `Card {}` declaration, separated by a blank line from the prior card.

2. **Rename the working file** to indicate finalization:
   - `encoding-artifacts/working-<ts>.ccgnf` → `encoding-artifacts/cluster-<ts>.ccgnf`

3. **Regenerate `encoding/cards/DISTRIBUTION.md`**:

   ```bash
   make card-distribution
   # or, if make isn't available:
   python3 tools/update-card-distribution.py
   ```

   The Python script is the real worker; `make card-distribution` is just a wrapper. Either is fine. The script scans every faction file, counts rarity × faction, handles dual pairs, and rewrites the tables.

   Two tables live in DISTRIBUTION.md: the legacy rarity × faction table, and — as of step 12.3 — a role × faction table with the closer / setup / disruption / filler counts. The next audit at `docs/plan/balance/card-pool-<date>.md` reads from that second table, closing the audit → skill → audit loop. If the regeneration script hasn't grown the role column yet, update it (`tools/update-card-distribution.py`) before finalizing.

4. **Run the encoding corpus test** to confirm the appended cards still parse cleanly when combined with the rest of the set:
   ```bash
   dotnet test --filter "EncodingCorpusTests" --nologo
   ```

5. **Ask the user**: "Ready to commit and push?"

   If yes:
   ```bash
   git add encoding/cards/ encoding-artifacts/cluster-<ts>.ccgnf
   git commit -m "Add <N>-card cluster (seed <seed>): <short summary>"
   git push origin main
   ```

   If no: leave changes on disk and say where they are. The user can edit or commit manually.

## Distribution file format

`encoding/cards/DISTRIBUTION.md` follows this structure:

```markdown
# Resonance — Card Set Distribution

*Regenerated by the `card-cluster` skill after each approved cluster.*

Target (per `design/Supplement.md §4`, 250-card set):
- Common: 44% (110 cards)  |  Uncommon: 32% (80 cards)
- Rare:   18%  (45 cards)  |  Mythic:   6%  (15 cards)

## Current counts — mono-faction

| Faction  | C | U | R | M | Total | % of set |
|----------|---|---|---|---|-------|----------|
| EMBER    | 4 | 2 | 1 | 1 |   8   |   X%     |
| ...      |   |   |   |   |       |          |

## Dual-faction

| Pair            | C | U | R | M | Total |
|-----------------|---|---|---|---|-------|
| EMBER/THORN     | 0 | 1 | 0 | 0 |   1   |
| TIDE/HOLLOW     | 0 | 0 | 1 | 0 |   1   |

## Neutral

| Rarity | Count |
|--------|-------|
| C      |   3   |
| ...    |       |

## Grand total: N cards

Last updated: <iso8601-utc>
```

## Conventions and invariants

- **Working files are not committed.** The `.gitignore` has a rule for `encoding-artifacts/working-*.ccgnf`. Only `cluster-*.ccgnf` (finalized) gets tracked.
- **Cards never move between faction files after approval.** If a card needs re-factioning, that's a separate design action.
- **One-word name collision policy**: if a proposed name equals an existing card's name (case-insensitive), re-roll with the next PRNG stream offset.
- **Encoding corpus test is the final gate**. If appending cards breaks the corpus test, the cluster cannot be committed as-is — roll back the append, surface the error.

## Error handling

| Condition                                       | Action                                           |
|-------------------------------------------------|--------------------------------------------------|
| Toolchain build fails                           | Stop; show the build error; ask user to resolve. |
| Parser doesn't converge after 10 iterations     | Stop; show the final error set; ask for guidance. |
| Sync mismatch after edit                        | Fix immediately; treat as a bug, not a feature.  |
| Appended cards break the corpus test            | Roll back the append; report the offending card. |
| User rejects a card mid-cluster                 | Re-roll that slot (PRNG seed += 1000 + slot_idx) without invalidating other slots. |

## Example session

```
User: Make me 5 new BULWARK commons focused on attrition.

Skill:
  - Reads DISTRIBUTION.md; BULWARK C currently 4.
  - Toolchain: already Release-built, skip.
  - Seed: 1714572600 (from timestamp).
  - PRNG rarity: all 5 C (user override).
  - PRNG faction: all 5 BULWARK (user override).
  - PRNG type: [Unit, Unit, Unit, Maneuver, Standard].
  - PRNG keyword pick: [Fortify, Sentinel, Mend, Mend, Fortify-on-Standard].
  - LLM names each; checks for collision with existing BULWARK cards.
  - Writes encoding-artifacts/working-20260417T210000.ccgnf.
  - Validates — 0 diagnostics.
  - Sync-checks — all aligned.
  - Presents the 5 cards in Supplement style.

User: Change card 3's cost to 2.

Skill:
  - Updates both the block comment and the Card{...} cost field.
  - Re-validates — clean.
  - Re-presents.

User: Looks good.

Skill:
  - Appends all 5 to encoding/cards/bulwark.ccgnf.
  - Renames working file to cluster-20260417T210000.ccgnf.
  - Regenerates DISTRIBUTION.md (BULWARK C is now 9).
  - Runs corpus test — green.
  - Asks: "Ready to commit and push?"

User: Yes.

Skill:
  - git add encoding/cards/ encoding-artifacts/cluster-20260417T210000.ccgnf
  - git commit -m "Add 5-card BULWARK commons cluster (seed 1714572600): attrition set"
  - git push origin main
```
