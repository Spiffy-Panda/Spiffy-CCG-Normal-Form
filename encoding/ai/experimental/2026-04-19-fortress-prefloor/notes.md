# Fortress (pre-floor snapshot)

Frozen copy of `2026-04-19-fortress` *before* the Step 12.1 floor-rule
edit on 2026-04-20. Kept solely as the head-to-head opponent for the
bench run that justified the edit. See the sibling
`2026-04-19-fortress/` for the current Fortress weights and
`docs/plan/balance/ai-floor-2026-04-20.md` for the bench result.

This profile's `pushing` intent violates the
`conduit_softness ≥ threat_avoidance` floor rule on purpose — it exists
to let future sessions reproduce the pre-floor bench without having to
dig in git history.

Do not evolve this profile; if the live Fortress ever needs another
re-bench, snapshot the live weights into a new dated `-prefloor-N`
sibling instead of overwriting this one.
