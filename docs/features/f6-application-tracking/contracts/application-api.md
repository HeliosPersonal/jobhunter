---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "06-07"
ticket: ""
tags: [sdlc/stage-06, feature/f6-application-tracking, mvp, jobhunter]
---

# Application API

> Owner-scoped endpoints on `jobhunter-api`. Every one declares its scope explicitly; the F0
> fallback-deny policy means an endpoint added without one is refused by default.

## Endpoints

| Method | Path | Scope | Purpose |
|---|---|---|---|
| `GET` | `/api/applications` | `jobhunter:read` | Pipeline, grouped by status |
| `GET` | `/api/applications/{id}` | `jobhunter:read` | One application with its full history and notes |
| `POST` | `/api/applications/{id}/status` | `jobhunter:admin` | Change status |
| `POST` | `/api/applications/{id}/notes` | `jobhunter:admin` | Attach a note |
| `GET` | `/api/applications/due` | `jobhunter:read` | What needs attention now |

## Pipeline response

```json
{
  "counts": { "saved": 12, "applied": 8, "interview": 3, "offer": 0, "rejected": 21 },
  "groups": [
    {
      "status": "Interview",
      "applications": [
        {
          "id": "0192f3a1-...",
          "jobId": "0192e8b7-...",
          "title": "Staff Backend Engineer",
          "company": "Snowflake",
          "score": 95,
          "postingClosed": false,
          "appliedAt": "2026-07-14T09:12:00Z",
          "lastActivityAt": "2026-07-28T16:40:00Z",
          "nextActionAt": "2026-08-04T08:00:00Z",
          "daysInStage": 5
        }
      ]
    }
  ]
}
```

`daysInStage` is computed rather than stored, because it is a presentation concern and storing it
would mean keeping it current.

## Status change

```json
POST /api/applications/{id}/status
{ "toStatus": "Interview", "detail": "first call scheduled" }
```

| Outcome | Response |
|---|---|
| Permitted | `200` with the updated application |
| Not permitted | `409` naming the attempted transition and why it is impossible |
| Application not found | `404` |
| Missing scope | `403` |

The `409` body states the rule, not just the refusal:

```json
{
  "error": "TransitionNotPermitted",
  "from": "Rejected",
  "to": "Interview",
  "reason": "An application cannot return to Interview after Rejected. Create a new application if the company re-opened the conversation."
}
```

Only genuinely impossible sequences are refused ([[../adr/0001-permissive-transitions-with-history|ADR-F6-0001]]);
the message says what to do instead, because a refusal without a remedy is just an obstacle.

## Transition matrix

Rows are the current status, columns the target. `✓` permitted, `—` refused.

| From \ To | Saved | Applied | Interview | Rejected | Offer | Ignored |
|---|---|---|---|---|---|---|
| **New** | ✓ | ✓ | ✓ | ✓ | — | ✓ |
| **Saved** | — | ✓ | ✓ | ✓ | — | ✓ |
| **Applied** | ✓ | — | ✓ | ✓ | ✓ | ✓ |
| **Interview** | — | — | ✓ | ✓ | ✓ | ✓ |
| **Rejected** | — | ✓ | — | — | — | ✓ |
| **Offer** | — | — | — | ✓ | — | — |
| **Ignored** | ✓ | ✓ | — | — | — | — |

Notes on the less obvious cells:

- `New → Interview` is permitted: an inbound approach happens, and the Owner should not have to fake
  an `Applied` step to record it.
- `Interview → Interview` is permitted: multiple rounds are one stage, and the transition record is
  how the rounds are visible.
- `Applied → Saved` is permitted as a correction — a mis-tap should be fixable.
- `Rejected → Applied` is permitted: companies re-open roles, and the history will show it clearly.
- `Offer → Rejected` is permitted: the Owner declining is an outcome worth recording.
- `Offer → Ignored` is refused. An offer is not something you ignore; it is accepted or declined, and
  `Rejected` covers declining.

The matrix is enumerated by the test suite, so all 36 pairs are covered rather than the handful
someone thought of.

## Related

[[../sad]] §4 · [[../test-plan]] · [[../../f9-search-and-api/contracts/openapi|F9 OpenAPI]]
