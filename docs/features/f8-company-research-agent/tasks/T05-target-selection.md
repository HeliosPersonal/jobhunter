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
