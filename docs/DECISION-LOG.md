---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
stage: "00"
ticket: ""
tags: [decision-log, jobhunter]
---

# DECISION LOG

> Cross-cutting product and process decisions that are **not** architecture decisions.
> Architecture goes in [[00-overview/adr/0001-modular-monolith-three-deployables|ADRs]]; this log
> holds the choices that shape scope, cadence, UX and working method.
>
> Legend: ✅ chosen · ⚖️ weighed alternative · ❌ rejected

---

### D1 · Who is this for

> → **Decision (2026-08-02):** Single Owner, plus the reviewer as an explicit secondary audience ✅

- [x] ✅ **One Owner; the repository is also a portfolio artifact.**
  *Why:* the product need is real and singular; building for a hypothetical second user would
  double the surface (tenancy, auth, onboarding) for zero present value. Treating the reviewer as a
  real user is what justifies the documentation investment.
  *Research:* [[00-overview/idea-brief]] §3.
- [ ] ⚖️ Build multi-tenant from the start. *Why not:* every table gets a tenant column and every
  query a filter, permanently, for a user who does not exist. The architecture does not *forbid* it later.
- [ ] ❌ Ignore the reviewer audience. *Why not:* half the project's return is the portfolio value;
  ignoring it would justify skipping the docs, which is exactly the failure mode of solo projects.

---

### D2 · Delivery cadence and surface

> → **Decision (2026-08-02):** One digest, 07:00 Europe/Kyiv, Telegram only ✅

- [x] ✅ **A single daily digest at 07:00, delivered to Telegram, never delayed.**
  *Why:* the product exists to *remove* interruption. A daily rhythm is the feature, not a
  limitation. If a Batch is unfinished at 07:00 the digest ships partial with an explicit note
  rather than arriving late.
- [ ] ⚖️ Push notification per high-scoring job. *Why not:* recreates the interruption pattern the
  project exists to eliminate; also makes every scoring false-positive maximally annoying.
- [ ] ❌ Email and Slack channels alongside Telegram. *Why not:* an abstraction cost with no second
  reader. Parked in [[00-overview/idea-brief]] §14 item 9.

---

### D3 · Where jobs come from

> → **Decision (2026-08-02):** ATS APIs only; no scraping ✅ — formalised as
> [[00-overview/adr/0009-ats-first-no-linkedin|ADR-0009]]

- [x] ✅ **Greenhouse, Lever, Ashby, Workable first; JSON-LD career pages second; nothing else.**
  *Why:* stable public JSON, earlier inventory than aggregators, no ToS exposure, and the repository
  stays publishable.
- [ ] ❌ LinkedIn via headless browser. *Why not:* anti-bot arms race, ToS exposure, brittle, and it
  would make the portfolio artifact a liability rather than an asset.
- [ ] ⚖️ A paid aggregator API. *Why not:* duplicates ATS content, adds cost and staleness. Revisit
  only if ATS coverage proves insufficient after M2.

---

### D4 · How much does a day of intelligence cost

> → **Decision (2026-08-02):** Hard ceiling of $2.00 per Run; expected operating point ≈ $1.03 ✅

- [x] ✅ **A pre-submission cost ceiling that aborts the Stage, enforced in code.**
  *Why:* an alert after the fact is not a control. [[CONTEXT]] invariant 6 makes this a correctness
  property, not a budgeting preference, which is why it is testable.
  *Numbers (verified 2026-08-02):* at 150 jobs/day a Run costs ≈ **$2.16** as first designed and
  ≈ **$1.03** with the F4 pre-match filter and CV prompt caching — about **$31/month**. The $2.00
  ceiling is therefore real headroom over the optimised figure and roughly break-even against the
  naive one, which is the intended shape: it catches a runaway without clipping a normal day.
  Breakdown: [[operations/infrastructure]] §8.
- [ ] ⚖️ Monthly budget alert only. *Why not:* a retry storm can spend a month's budget in an hour.
- [ ] ❌ No limit. *Why not:* the single most likely way this project becomes a source of anxiety
  rather than a source of jobs.

---

### D5 · What "good" means, and how it is measured

> → **Decision (2026-08-02):** `precision@10`, rated by the Owner, tracked from the first digest ✅

- [x] ✅ **≥6 of the top 10 Cards rated "worth opening", measured weekly.**
  *Why:* one number, directly felt, and it forces the ranking to be honest. Volume metrics
  (jobs discovered, jobs enriched) are diagnostics, never goals.
  *Research:* [[00-overview/idea-brief]] §8 Executive.
- [ ] ⚖️ Application-conversion rate. *Why not:* too slow a feedback loop (weeks) and confounded by
  everything outside the system's control.
- [ ] ❌ Jobs surfaced per day. *Why not:* rewards exactly the noise the product exists to remove.

---

### D6 · Does preference learning ship in the MVP

> → **Decision (2026-08-02):** Signal capture ships with F5; the learned model ships in M5 ✅

- [x] ✅ **Record Signals from the very first digest; activate the PreferenceModel once ≥200 Signals exist.**
  *Why:* the data is unrecoverable if not captured from day one, but fitting weights on 12 Signals
  would produce confident nonsense. Splitting capture from inference gets both right.
- [ ] ⚖️ Ship learning immediately. *Why not:* no evidence to learn from; the model would encode the
  first week's accidents.
- [ ] ❌ Defer Signal capture too. *Why not:* six weeks of preference evidence thrown away, and the
  UX promise ("ignoring teaches the system") never becomes true.

---

### D7 · Suppression must be visible

> → **Decision (2026-08-02):** Learned filters suppress with a reason, and the digest reports it ✅

- [x] ✅ **Every suppressed Job records a reason; the digest footer states how many and why.**
  *Why:* a silent filter is indistinguishable from a bug, and it is the fastest way to lose trust in
  an automated system. It is also the strongest retention mechanism available — "I stopped showing
  you 34 jobs below your salary floor" makes ignoring feel productive.
  *Research:* [[00-overview/idea-brief]] §8 UX-researcher. Encoded as [[CONTEXT]] invariant 11.
- [ ] ❌ Silent hard filters. *Why not:* unfalsifiable. The Owner can never tell a good filter from a broken one.

---

### D8 · Documentation method

> → **Decision (2026-08-02):** Docs-first SDLC with readiness gates ✅

- [x] ✅ **Per feature: idea-brief → PRD → SAD → data-model → contracts → tasks → test-plan, gated by
  [[IMPLEMENTATION-READINESS]].** No task starts before its feature's PRD, SAD, data-model and
  test-plan are accepted.
  *Why:* it is the mechanism that keeps a solo project from rotting (risk R7), and it is the artifact
  the reviewer audience actually reads. Pattern taken from the `sentra` and `wisewizard` projects.
- [ ] ⚖️ Lightweight README-per-feature. *Why not:* insufficient to drive implementation and
  worthless as a portfolio signal.
- [ ] ❌ Code first, document after. *Why not:* the "after" never arrives on a part-time solo project.

---

### D9 · Testing bar

> → **Decision (2026-08-02):** >90% line and branch, CI-gated, composition roots excluded ✅

- [x] ✅ **Coverlet threshold at 90 for line and branch; `Api`/`Worker`/`Telegram` `Program.cs` and
  the Aspire AppHost excluded; integration tests against real PostgreSQL via Testcontainers.**
  *Why:* `wisewizard` proved a >95% gate is sustainable for a pure-logic codebase, but JobHunter has
  substantially more adapter surface (five ATS clients, Telegram rendering, Typesense). 90% is the
  honest number that stays green without writing tests for the sake of the gate.
- [ ] ⚖️ 95% as in `wisewizard`. *Why not:* would push toward testing wiring rather than behaviour.
- [ ] ❌ No coverage gate. *Why not:* R7. The gate is the thing that makes refactoring safe six weeks later.

---

### D10 · Milestone shape

> → **Decision (2026-08-02):** Ship a usable digest at M4 (~week 7), then compound ✅

- [x] ✅ **M1 skeleton → M2 inventory → M3 intelligence → M4 the product → M5 compounding.**
  *Why:* the portfolio argument has a shelf life and a half-built pipeline demonstrates nothing.
  M4 is a real product; F6–F9 make it better rather than making it work.
  *Research:* [[00-overview/idea-brief]] §13, tracked in [[BACKLOG]].
- [ ] ❌ Build all nine features before the first delivery. *Why not:* risk R6 — the most common way
  a project like this dies.

---

### D11 · Language and localisation

> → **Decision (2026-08-02):** English everywhere, in code and in docs ✅

- [x] ✅ **English for identifiers, comments, logs, docs and digest copy.**
  *Why:* the reviewer audience is international, and mixed-language codebases age badly. The one
  bilingual artifact is [[DECISIONS-MATRIX.uk|`DECISIONS-MATRIX.uk.md`]] — a single Ukrainian
  reconfiguration menu of every decision and tunable, indexed in the README. It exists to serve a
  stakeholder conversation, and it is the only `.uk.md` tier; the code, logs and per-feature docs
  remain English-only.
- [ ] ⚖️ Ukrainian summaries alongside English, as `sentra` does. *Why not:* doubles the doc
  maintenance surface for an audience of one who reads English fine.

---

## Related

- [[DECISIONS-MATRIX.uk|Матриця рішень]] — every decision here, plus all ADRs and tunables, as a
  reconfiguration menu (Ukrainian)
- [[CONTEXT]] · [[00-overview/idea-brief]] · [[00-overview/sad]]
- [[BACKLOG]] · [[IMPLEMENTATION-READINESS]] · [[ARCHITECTURE-OPEN-DECISIONS]]
