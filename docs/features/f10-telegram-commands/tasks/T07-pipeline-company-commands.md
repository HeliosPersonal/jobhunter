# T07 — Pipeline and company commands

**Layer:** telegram · **Deps:** T05 · **Est:** M · **Owner:** Viacheslav

## What

`/saved`, `/pipeline`, `/due`, `/note`, `/company`, `/research`. Pipeline entries carry
buttons for their legal next transitions, so advancing costs one tap and no second command.

## Done when

- `/pipeline` groups by status with counts and offers only legal transitions per the F6 matrix (AC-03).
- `/note` with no text enters the multi-step flow; with no recent application it offers the last five.
- Note content is never logged — only its length.
- `/company` resolves names and domains forgivingly; an ambiguous name offers both.
- An unknown company offers to add it rather than returning empty (AC-11); a known company without a dossier offers `/research`.
- `/research` confirms with the age of any existing dossier, so a needless refresh is visible before it is paid for.

## Implementation

Five commands over three read services and one write port, each behaviour owned by the feature it
belongs to; F10 adds the Telegram-facing rendering and the read/write split the catalogue mandates.

**`/pipeline` and `/due`.** `/pipeline` groups the tracked applications by status through
`IApplicationPipelineQuery`, renders a bold "status — count" header per group, and hangs each
application's **legal next transitions** off it as inline callback buttons (`st:{target}:{applicationId}`),
so advancing a stage costs one tap and no second command (AC-03). The transitions offered are exactly
the F6 status matrix — F6 owns which moves are legal, F10 only renders them. `/due` reads the
past-threshold applications through `IDueReminderQuery` and renders them through F6's shared
`IReminderRenderer`, so the "past its stage threshold" wording is identical to the 08:00 reminder sweep
rather than a second copy. Both read a clock only through `IClock.UtcNow`, so `DateTime.UtcNow` never
appears (architecture rule 5).

**`/note`.** With text, the note is written straight to the most-recently-active application through the
shared `AddNoteHandler` — the one write path the API uses too — and the confirmation *names* the
application it landed on, never a bare "done". With no text it enters the multi-step flow: it stores a
short-lived per-chat `ConversationState` awaiting the note and asks for it, writing nothing yet (AC-08).
The stored state carries only the target job id — an id, never any content the Owner typed. The note
body is **never logged**, only its length (invariant 12); the confirmation is derived from the
application, not the body. With no application to attach to, it says so plainly rather than failing
silently.

**`/company` and `/research` — the read/write split.** These are twins over the same forgiving
resolution, `ICompanyResearchQuery.ResolveCandidatesAsync`, which matches a display name (`Stripe`), a
canonical domain (`stripe.com`) and a bare registrable label (`stripe`) alike, ordered
most-recently-seen first so an ambiguity surfaces the freshest first. A `WHERE`-OR returns each row at
most once however many clauses it satisfies, so a query matching by both name and label still yields one
candidate. Both commands answer an unknown company by **offering to add it** to the registry rather than
returning empty (AC-11), and a query matching more than one company by **naming every match** so the
Owner can pick — never a silent resolution to the first.

`/company` is **read-only** (catalogue §Company · State read): a single resolved company with a fresh
dossier is rendered through F8's shared `DossierFormatter` with its age; a dossier that is stale or
absent is answered with an offer to `/research`, never a queue write from here. `/research` **owns the
queue write** (State ✎, F8 AC-05): a single resolved company whose dossier is absent or stale is enqueued
through `IResearchRequestWriter` for the next cycle — idempotent per company per cycle — and acknowledged
with "tomorrow's digest", because research is batched and cost-ceilinged, never interactive. A dossier
that is still fresh is **not** re-queued: its freshness is reported so a needless refresh is visible
before it is paid for. Freshness is judged with the domain `Freshness` policy against `IClock`, so the
stale/fresh boundary is deterministic and a volatile category (news, layoffs) pulls the whole refresh
forward. Neither command runs an LLM or touches the CV (the CV crosses exactly one boundary, and it is
not this one), and every dynamic value — company names included — reaches the reply through the one
MarkdownV2 escaper, so a hostile name renders literally.

**Deferred to T10.** As with T03–T06, this task ships the mechanism, not the live callback wiring. The
`/pipeline` transition buttons carry their `st:` tokens and the `/note` multi-step flow stores its
pending state, but the routing that resumes a stored state on the next free-text message and drives a
transition tap back to the F6 status-change path is wired with the dispatch rewire against the full
command registry (T10). The reads, writes and rendering here are live; only the callback routes are
deferred.

## Links

[[../contracts/command-catalogue|catalogue]] §Pipeline, §Company · [[../../f6-application-tracking/index|F6]] · [[../../f8-company-research-agent/index|F8]]
