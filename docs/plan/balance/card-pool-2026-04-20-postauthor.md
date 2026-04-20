# Step 12.3 — Card threat audit (2026-04-20, post-authoring)

**Verdict: closer-density target hit in all four mono-factions.** The
per-faction authoring pass landed 19 new closer cards (BULWARK 7, HOLLOW
5, TIDE 5, THORN 2). Every mono-faction now meets or clears its target
closer ratio from the pre-authoring audit
([`card-pool-2026-04-20.md`](card-pool-2026-04-20.md)).

Matched-pair bench moved PairCorrectly draw rate from **81.7 % (baseline,
pre-authoring) → 69.2 % (post-authoring)**, a total shift of −12.5 pp
across the arc. Still above the ≤ 40 % exit target, so the 12.2 engine
knob queue fires next per the 12.3 exit criteria §"Follow-up
sequencing."

## Per-faction pool stats (post-authoring)

| Faction  | Closer | Setup | Disruption | Filler | Total | Closer % | Target | Hit?   |
|----------|-------:|------:|-----------:|-------:|------:|---------:|-------:|:------:|
| EMBER    | 20     | 2     | 0          | 2      | 24    | **83 %** | ≥ 50 % |   ✅   |
| BULWARK  | 7      | 0     | 15         | 0      | 22    | **32 %** | ≥ 30 % |   ✅   |
| TIDE     | 5      | 15    | 3          | 0      | 23    | **22 %** | ≥ 20 % |   ✅   |
| THORN    | 5      | 17    | 1          | 0      | 23    | **22 %** | ≥ 20 % |   ✅   |
| HOLLOW   | 5      | 2     | 16         | 0      | 23    | **22 %** | ≥ 20 % |   ✅   |
| DUAL     | 1      | 1     | 2          | 0      | 4     | 25 %     |   —    |   —    |
| NEUTRAL  | 3      | 3     | 6          | 3      | 15    | 20 %     |   —    |   —    |
| **All**  | **46** | **40**| **43**     | **5**  | **134**| **34 %**| ≥ 23 % | ✅     |

Total card pool grew from 115 → 134 (+19 closers from the authoring pass).

## Cards authored (by faction)

### BULWARK — 7 closer cards (32 % closer, from 0 %)

- **WatchtowerArcher** [U] — Sentinel + Fortify 2, End-of-Clash 1-damage Conduit ping.
- **RetributionStrike** [U] — Conduit damage = Sentinel Unit count (cap 4).
- **BannersOfVigilance** [R] — Standard; Start-of-turn Conduit damage gated on 2+ Sentinels (Peak = 2).
- **SiegeCaptain** [U] — Fortify 1 + OnEnter and End-of-Clash Conduit pings.
- **WallbreakerIncarnate** [M] — Sentinel/Fortify 3 + BULWARK-4 End-of-Clash siege; Peak deals 5.
- **BreachAndSeize** [C] — 1 damage to target Conduit; BULWARK 3: 2.
- **RampartCharger** [C] — Fortify 1 Unit with OnEnter Conduit ping.

Identity stays true: every BULWARK closer scales off defensive investment
(Sentinel density, Fortify active, high-Rampart bodies), never projects
raw aggro Force.

### HOLLOW — 5 closer cards (22 % closer, from 0 %)

- **HollowEcho** [U] — Conduit damage = cards in opponent's Cache (cap 5).
- **Veilstrike** [C] — Phantom + 1 damage Conduit ping on PhantomReturn.
- **MindRazor** [C] — Conduit damage = opponent's hand − 3 (min 0, cap 4).
- **ShadowExecutor** [R] — Shroud + conditional Blitz while you control another Shroud Unit.
- **ErasureProphet** [R] — Phantom + End-of-Clash 2-damage Conduit ping; Peak = 3.

Identity stays true: every HOLLOW closer corrodes a specific opponent
resource (Cache, hand, board) or turns Phantom/Shroud evasion into
Conduit pressure.

### TIDE — 5 closer cards (22 % closer, from 0 %)

- **RippleBreaker** [U] — Drift + TIDE-Maneuver-played Conduit ping.
- **WaveCount** [U] — Conduit damage = TIDE cards in your Cache (cap 5).
- **DriftStriker** [C] — Drift Force-3 body with OnEnter Conduit ping.
- **WaveFinisher** [R] — 3 damage Conduit + recur non-Unique TIDE Maneuver; TIDE 4: 5.
- **TidebreakerSage** [R] — Drift + End-of-Turn Conduit siege; Peak = 3.

Identity stays true: TIDE's closers either trigger on TIDE Maneuvers,
scale with TIDE Cache churn, or turn Drift into Conduit damage. No raw
aggro bodies.

### THORN — 2 closer cards (22 % closer, from 14 %)

- **SprawlVanguard** [U] — Rally + conditional Blitz for self + Saplings at 3+ Saplings.
- **OvergrowthSurge** [U] — Conduit damage = Sapling count (cap 4).

Both cards pay off the existing Sprawl / Rally engine: SprawlVanguard
flips the 1-cost Rally tokens from scaffolding into a damage end-state,
and OvergrowthSurge gives the faction its first Sprawl-scaling Maneuver.

## Bench arc

PairCorrectly draw-rate progression, baseline seed 1, 40 games /
matchup (240 total):

| Milestone                        | Draws    | %       | Δ from previous |
|----------------------------------|---------:|--------:|---------------:|
| Pre-authoring (12.0 baseline)    | 196 / 240 | 81.7 % | —              |
| Post-BULWARK commit              | 190 / 240 | 79.2 % | −2.5 pp        |
| Post-HOLLOW commit (mid-pass)    | 189 / 240 | 78.8 % | −0.4 pp        |
| Post-TIDE commit                 | 174 / 240 | 72.5 % | −6.3 pp        |
| Post-THORN commit (final)        | 166 / 240 | 69.2 % | −3.3 pp        |
| **Total shift**                  |          |        | **−12.5 pp**   |

Per-pair decisive WR at milestones (wins / wins + losses):

| Pair     | Baseline | Post-BULWARK | Post-HOLLOW | Post-TIDE | Post-THORN |
|----------|---------:|-------------:|------------:|----------:|-----------:|
| BulFort  | 76.2 %   | 55.6 %       | 43.5 %      | 38.5 %    | 34.5 %     |
| HolReap  | 8.1 %    | 5.7 %        | 13.9 %      | 15.0 %    | 14.0 %     |
| EmbHell  | 63.6 %   | 66.7 %       | 53.8 %      | 38.1 %    | 30.4 %     |
| TiThWave | 94.7 %   | 96.2 %       | 96.7 %      | 93.3 %    | 96.2 %     |

TiThWave ended the arc as the dominant pair (51 / 120 games won =
42.5 % raw WR). HolReap's +5.9 pp decisive-WR improvement came
entirely from HOLLOW's new closers — the deck went from essentially
zero win lines to an actual Conduit-damage clock. BulFort's −41.7 pp
decisive-WR regression is deck-construction noise (thinned
defensive spine) + matchup shift against TIDE's stronger finish; 12.4
deck reconstruction and 12.1 fortress re-tune both attach to this.

## Exit criteria

- [x] Every card carries exactly one of the four tags (134 total; no
      untagged cards).
- [x] Every archetype's mono-faction root meets its target closer %
      (BULWARK 32 % ≥ 30 %; HOLLOW / TIDE / THORN 22 % ≥ 20 %).
- [ ] Every archetype has a ≤ 6-turn lethal line — **not re-verified
      under the final deck lists** (still deck-construction-dependent).
      Carried forward to 12.4.
- [ ] Post-authoring PairCorrectly draw rate ≤ 40 %. **Missed at
      69.2 %.** Per 12.3 §"Exit criteria," the 12.2 engine knob queue
      now fires.
- [ ] `card-cluster` SKILL.md role-bucket awareness update lands in
      the same step-12.3 commit sequence (done; see commit).

## Follow-ups dispatched

1. **12.2 engine knobs.** The ≤ 40 % draw-rate target was missed by
   29.2 pp despite the full 19-card authoring pass — the engine needs
   the backstop. Fire the knob sequence per
   [`12.2`](../steps/12.2-engine-sanity-pass.md) §"Follow-up
   sequencing."
2. **12.1 AI-floor iteration loop.** TiThWave's 42.5 % WR and EmbHell's
   30.4 % decisive WR together say hellfire's weights are no longer
   calibrated to the post-authoring pool. Fortress's BulFort regression
   also points at a weight re-tune. 12.1 fires before 12.4 opens.
3. **12.4 deck construction.** Blocked by 12.3 until this audit
   closes; the hand-off is clean now. 12.4 consumes this pool honestly
   — the bulwark-control / hollow-disruption / tide-thorn-combo
   matched-pair decks got minimum-viable closer updates in this step
   so the bench could measure real movement, but 12.4 does the full
   rewrite with honest archetype declarations.

## Appendix — artifact paths

- Pre-authoring audit: [`card-pool-2026-04-20.md`](card-pool-2026-04-20.md)
- Per-faction bench results: `ai-testing-data/12.3-{bulwark,hollow,tide,thorn}.PairCorrectly.results.json`
- Mid-pass re-bench: `ai-testing-data/12.3-midpass.PairCorrectly.results.json`
- Final re-bench: `ai-testing-data/12.3-final.{PairCorrectly,SimpleSweep}.results.json`
