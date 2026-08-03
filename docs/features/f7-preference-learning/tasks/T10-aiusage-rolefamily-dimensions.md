# T10 — Add AiUsage and RoleFamily as preference dimensions

**Layer:** domain · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

The current dimensions (`SalaryBand, Country, CompanySize, Technology, TimezoneBand, RemotePolicy,
EmploymentType`) let the loop reinforce "more of what you clicked" but cannot reinforce the *career
trajectory*. Extend the closed `Dimension` enum with `AiUsage` and `RoleFamily` (the latter sourced
from the F3 enrichment signal, TUNE-03), so learning can pull toward the target — still under the
existing ≥3-signal evidence floor and ≤0.40 weight bound. Ensure `job_facts` snapshots capture both new
dimensions so signals carry the facts learning needs.

## Done when

- The closed `Dimension` enum gains `AiUsage` and `RoleFamily`; it remains a closed enum.
- `job_facts` snapshots include AiUsage and RoleFamily so a signal without them teaches nothing (AC).
- The WeightFitter treats the new dimensions under the same ≥3-supporting-signal floor and ≤0.40 bound.
- The synthetic corpus, including the indifferent profile that must produce no weights, stays green.
- Persistence (`preference_weights.dimension`) accepts the new values; the migration applies on a clean
  database.

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-08 ·
[[../data-model]] §preference_weights · [[T04-weight-fitter|T04 WeightFitter]]
