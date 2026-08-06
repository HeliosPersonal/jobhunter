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

## Implementation

- **C1 — the dimensions.** The closed `Dimension` enum (`src/JobHunter.Domain/Preferences/`) gains `AiUsage`
  and `RoleFamily`. Both the `WeightFitter` and the `PreferenceComponentCalculator` iterate
  `JobFacts.Dimensions` generically, so the new members flow through the same ≥3-signal evidence floor
  (`PreferenceWeight.MinSupportingSignals`) and the ≤0.40 dimension bound with no fitter change — proven by a
  `WeightFitterTests` case that recovers a positive `RoleFamily=AiPlatform`, a negative
  `RoleFamily=EnterpriseCrud` and a positive `AiUsage=High` weight, each in range and citing its signals.
- **C2 — the facts.** `JobFactsSnapshotQuery` (`src/JobHunter.Infrastructure/Persistence/Queries/`) projects
  the latest enrichment's `ai_usage` and `role_family` columns into the `AiUsage`/`RoleFamily` dimensions,
  alongside company stage and timezone band. Absent when the job was never enriched — a signal captured
  before enrichment teaches nothing on these — asserted by the "no enrichment" case.
- **No migration.** `preference_weights.dimension` is a free-text `text` column (enums persist as text, no
  CHECK constraint), and `enrichments.ai_usage` / `enrichments.role_family` already exist from F3. So the
  persistence already accepts the new values; T10 adds no migration.

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-08 ·
[[../data-model]] §preference_weights · [[T04-weight-fitter|T04 WeightFitter]]
