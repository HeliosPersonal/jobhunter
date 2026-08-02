# T10 — Index scan, rebuild and convention suites

**Layer:** tests · **Deps:** T05, T06, T07, T08 · **Est:** L · **Owner:** Viacheslav

## What

The three suites this feature's safety rests on: the no-CV-in-index scan, the rebuild
equivalence test, and the endpoint-convention test that makes an unprotected endpoint a build failure
rather than a security finding.

## Done when

- The index scan finds zero CV sentinels and asserts the field set exactly equals `JobDocument`'s (AC-04, QG-2).
- The rebuild test asserts document-by-document equivalence after a full drop and rebuild (AC-10, QG-1).
- The convention test asserts every endpoint except health declares a scope (AC-06, gate G7).
- The convention test asserts the OpenAPI document covers every registered endpoint with an example (AC-05).
- The fault-injection test runs a full pipeline with the index down and requires a delivered digest (AC-09, QG-3).
- A deliberately unprotected endpoint added in a fixture makes the convention test fail — proving it can.

## Links

[[../test-plan]] · [[../../../IMPLEMENTATION-READINESS]] §2 gate G7
