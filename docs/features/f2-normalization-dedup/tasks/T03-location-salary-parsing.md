# T03 — Location, remote policy and salary parsing

**Layer:** app · **Deps:** T01 · **Est:** L · **Owner:** Viacheslav

## What

`LocationParser` (free text and structured input to a location set), `RemotePolicyResolver`
(explicit provider signal wins; otherwise inferred from location text; never inferred from the
description) and `SalaryParser` (ranges with k-suffixes, explicit currencies, periods, or an
unparseable string retained raw).

## Done when

- ≥ 90% of provider-fixture postings yield at least one structured location.
- Free-text forms such as Remote - EMEA, Berlin Germany, US (Remote) and Anywhere all parse to defensible location sets.
- Lever's workplace type and Ashby's remote boolean take precedence over any text inference.
- A salary of Competitive produces `salary_raw` only — never a zero and never a null-coerced range.
- An inverted range is swapped and the anomaly logged.
- An unrecognised currency leaves `salary_raw` populated and the structured fields null.

## Links

[[../../f1-ats-job-discovery/contracts/ats-endpoints|ATS endpoints]] · [[../sad]] §8
