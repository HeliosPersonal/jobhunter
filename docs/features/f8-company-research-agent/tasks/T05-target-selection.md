# T05 — Target selection and freshness

**Layer:** app · **Deps:** T02 · **Est:** M · **Owner:** Viacheslav

## What

`ResearchTargetSelector` consuming `RankingCompleted`: pick at most five companies with no
dossier or a stale one, plus any on-demand requests. Freshness is 30 days generally and 7 for news and
layoffs.

## Done when

- At most five automatic targets per day, chosen by score.
- A fresh dossier is not refetched; a stale one is (AC-06).
- Freshness boundaries are asserted at 29, 30 and 31 days, and at 6, 7 and 8 for news.
- On-demand requests are queued and acknowledged, and do not displace automatic targets (AC-05).
- A company already queued is not queued twice.

## Links

[[../sad]] §6.1

## Implementation

`ResearchTargetSelector` and its inputs live in `src/JobHunter.Application/Research/`. The selector is a
pure `static` function — the same shape as `WeightFitter` — so the ≤5 cap, the freshness boundaries and the
no-double-queue rule are all deterministic under test; the clock is read once at the edge (T08's handler,
consuming `RankingCompleted`) and the instant is passed in.

- **`ResearchCandidate`** — one company behind the day's top jobs, with its ranking `Score` and, if it has
  been researched before, a `DossierFreshness` (the latest dossier's `GeneratedAt` and the categories it
  covered). T08 projects this from `companies` and the latest `company_research` row; the selector never
  touches a database.
- **`ResearchTargetSelector.Select(candidates, onDemand, now)`** → `ResearchTargets` — filters to the
  companies that need research, orders by score descending, takes `MaxAutomaticTargets = 5`, then appends the
  on-demand requests that are neither already automatic nor already queued. The two lists are disjoint and
  each is duplicate-free, so no company is researched twice in a cycle (AC-05). Fresh companies are excluded
  *before* the five are counted, so a high-scoring fresh dossier never consumes a slot a stale one deserves.
- **Freshness composition** — a candidate needs research if it has no dossier, or its dossier is stale.
  Staleness reuses the domain `Freshness` policy across the categories the dossier actually covered: it is
  stale as soon as it is stale for the soonest-ageing one, so a dossier that surfaced `News` or `Layoffs`
  ages out at seven days while a firmographic-only one lasts thirty. A dossier that covered nothing is judged
  by the non-volatile default window.

The boundaries the test-plan calls for (29/30/31 for firmographic, 6/7/8 for a news-covering dossier) are
asserted directly on `Select`, on top of the domain-level `FreshnessTests` that already fix the per-category
thresholds. What T05 does **not** own: the `RankingCompleted` handler that reads the clock and builds the
candidates, the job→company projection, and the on-demand request queue — those are wiring that lands with
the orchestrator in T08, where a database is in play.
