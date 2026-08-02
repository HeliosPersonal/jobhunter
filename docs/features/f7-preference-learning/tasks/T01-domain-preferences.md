# T01 — Domain: Signal, PreferenceModel, PreferenceWeight

**Layer:** domain · **Deps:** — · **Est:** M · **Owner:** Viacheslav

## What

`Signal`, `SignalKind`, `PreferenceModel`, `PreferenceWeight` and `Dimension`. The
construction guard carries the ADR: a `PreferenceWeight` cannot be constructed with fewer than three
supporting signal ids, so the evidence floor is a type-level property rather than a validation step
that can be skipped.

## Done when

- Constructing a `PreferenceWeight` with fewer than 3 supporting signal ids throws (AC-03).
- `Signal` requires a non-empty `job_facts` snapshot — a signal without facts teaches nothing.
- Signal weights per kind match [[../sad|SAD]] §8 and come from configuration.
- `PreferenceModel` is immutable; activation is a separate operation.
- The seven dimensions are a closed enum.

## Links

[[../adr/0002-evidence-threshold-and-explainability|ADR-F7-0002]] · [[../data-model]]
