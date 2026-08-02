# T06 — Lever, Ashby and Workable adapters

**Layer:** scrapers · **Deps:** T05 · **Est:** L · **Owner:** Viacheslav

## What

The remaining three Tier-1 adapters against
[[../contracts/ats-endpoints|the endpoint reference]]. Each has one genuinely distinct concern:
Lever's `workplaceType` is the cleanest remote signal available; Ashby publishes structured
compensation (the only provider that routinely does, and worth extracting properly rather than
leaving to the model); Workable's `published_on` is date-only, which freshness ranking must tolerate.

## Done when

- All three adapters pass the full standard fixture set.
- Lever `workplaceType` maps to the remote-policy vocabulary without inference.
- Ashby compensation is parsed into structured salary; an unparseable tier string is retained raw, never coerced.
- Workable's date-only `published_on` is stored at midnight UTC and flagged as day-granular.
- Each adapter's volatile fields are documented in the contract file and stripped before hashing.

## Links

[[../contracts/ats-endpoints|ATS endpoints]] · [[../test-plan]]
