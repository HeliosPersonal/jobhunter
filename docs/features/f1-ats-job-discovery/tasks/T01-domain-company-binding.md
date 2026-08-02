# T01 — Domain: Company, AtsBinding, CanonicalDomain

**Layer:** domain · **Deps:** — · **Est:** M · **Owner:** Viacheslav

## What

`Company`, `AtsBinding`, `AtsKind`, `BindingConfidence` and the `CanonicalDomain` value
object. Canonicalisation is the subtle part: lowercase, strip scheme, strip `www.`, resolve to the
registrable domain using the public suffix list (so `careers.stripe.com` and `stripe.com` are one
company, but `foo.github.io` and `bar.github.io` are two).

## Done when

- `CanonicalDomain.TryCreate` handles subdomains, ports, trailing dots, punycode and uppercase.
- `stripe.com`, `www.stripe.com`, `https://Stripe.com/careers` all canonicalise identically.
- Public-suffix cases (`*.github.io`, `*.co.uk`) resolve to the correct registrable domain.
- `AtsBinding.Retire(clock)` sets `retired_at` and is idempotent.
- `Company.ActivateForDiscovery()` refuses when no binding has confidence ≥ 0.80.

## Out of scope

- Persistence — T02.
- Detection — T05.

## Links

[[../data-model]] · [[../../../CONTEXT]] §1
