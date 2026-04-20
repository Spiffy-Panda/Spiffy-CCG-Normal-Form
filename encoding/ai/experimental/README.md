# Experimental utility-bot profiles

This directory holds hand-tuned weight tables for the `UtilityBot`.
Each subdirectory is an independent experiment, identified by date +
concept slug. Profiles are auto-discovered by
`/api/ai/bots` and runnable through `/api/ai/tournament` as
`experimental/<slug>` when `CCGNF_AI_EDITOR=1`.

Promotion to `encoding/ai/stable/` is out of scope for anything in
this directory — that gate lives behind a benchmark step that hasn't
landed yet (see `docs/plan/steps/10.2-long-term-ai-plan.md`).

## Current catalogue

| Slug                        | Target deck         | One-line concept |
|-----------------------------|---------------------|------------------|
| `2026-04-19-fortress`       | `bulwark-control`   | Defence-first: dominant `threat_avoidance`, triple-weighted `lowest_live_hp`, negligible `tempo_per_aether`. |
| `2026-04-19-hellfire`       | `ember-aggro`       | All-in aggression: zero `threat_avoidance`, heavy `conduit_softness` + `opponent_priority`. |
| `2026-04-19-reaper`         | `hollow-disruption` | Removal-first: dominant `lowest_live_hp`, low `conduit_softness`, no closing instinct. |
| `2026-04-19-wavebreaker`    | `tide-thorn-combo`  | Pure efficiency: dominant `on_curve` + `tempo_per_aether`, refuses off-curve plays. |

Each profile's `notes.md` records the rationale, expected behaviour,
known weaknesses, and the tournament numbers captured at commit time.

## Adding a new profile

1. Pick a concept with an observable signature a playtester can
   describe. If you can't finish the sentence "a playtester would
   tell this bot from `utility` because …", pick a different concept.
2. Create `YYYY-MM-DD-<slug>/` under this directory.
3. Drop in `weights.json` (schema: `src/Ccgnf.Bots/Utility/WeightTable.cs`),
   `notes.md` (concept + expected results + weaknesses + post-run
   results), and `lineage.jsonl` (append-only save history; required
   shape in the "Resolved in this revision" section of the plan).
4. Rebuild; `/api/ai/bots` will auto-discover the profile via the
   scanner in `src/Ccgnf.Rest/Endpoints/AiEndpoints.cs`.
5. Run a mirror tournament against the target deck plus at least one
   off-target deck. Record step counts, not just win rates — the
   mirror harness loses most of its signal below the 500-step mark.

## Known harness limitation

`/api/ai/tournament` is a mirror-only round-robin today. Rows report
`<bot>-vs-<bot>`, not cross-profile head-to-heads. That means two
profiles with identical behavioural gradients on a given deck will
print the same row — divergence only shows up when the deck asymmetry
forces different decisions. Treat matching numbers as "no mirror
signal", not "profiles are identical". A head-to-head harness is on
the plan; wire concept tests to the step count and to observed
decision frames until that lands.
