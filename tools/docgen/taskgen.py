"""Shared generator for per-feature task files, epic and tracker."""
import pathlib

def fm(slug, stage, status="Draft", size="M"):
    return (f'---\nstatus: {status}\nowner: "Viacheslav Melnichenko"\n'
            f'reviewers: ["Tech Lead (Viacheslav)"]\nupdated_at: "2026-08-02"\n'
            f'feature_size: "{size}"\nstage: "{stage}"\nticket: ""\n'
            f'tags: [sdlc/stage-{stage.split("-")[0]}, feature/{slug}, mvp, jobhunter]\n---\n\n')

def write_tasks(slug, tasks):
    """tasks: list of dicts with id, title, file, layer, deps, est, what, done[], oos[], links"""
    base = pathlib.Path(f"docs/features/{slug}/tasks")
    base.mkdir(parents=True, exist_ok=True)
    for t in tasks:
        deps = ", ".join(t["deps"]) if t["deps"] else "—"
        body = [
            f'# {t["id"]} — {t["title"]}',
            "",
            f'**Layer:** {t["layer"]} · **Deps:** {deps} · **Est:** {t["est"]} · **Owner:** Viacheslav',
            "",
            "## What",
            "",
            t["what"].strip(),
            "",
            "## Done when",
            "",
        ]
        body += [f'- {d}' for d in t["done"]]
        if t.get("oos"):
            body += ["", "## Out of scope", ""] + [f'- {o}' for o in t["oos"]]
        body += ["", "## Links", "", t["links"].strip(), ""]
        (base / t["file"]).write_text("\n".join(body))
    return len(tasks)

def write_tracker(slug, title, tasks, mermaid, header_note, dod, epic_link="_epic"):
    base = pathlib.Path(f"docs/features/{slug}/tasks")
    rows = []
    for t in tasks:
        deps = ", ".join(t["deps"]) if t["deps"] else "—"
        stem = t["file"].removesuffix(".md")
        rows.append(f'| {t["id"]} | [[{stem}\\|{t["title"]}]] | {t["layer"]} | {deps} | {t["est"]} | pending |')
    total_s = sum(1 for t in tasks if t["est"] == "S")
    total_m = sum(1 for t in tasks if t["est"] == "M")
    total_l = sum(1 for t in tasks if t["est"] == "L")
    days = total_s * 0.25 + total_m * 0.5 + total_l * 1.0
    content = (fm(slug, "13") +
f'''# Task tracker — {title}

Epic: [[{epic_link}|_epic]]. {header_note}

Each task is one reviewable PR (≤500 LOC), ≤1 day. Owner: Viacheslav (solo).
Estimate legend: **S** ≈ 2 h · **M** ≈ half a day · **L** ≈ a full day.
Status: `pending` → `in_progress` → `in_review` → `done`.

| ID | Task | Layer | Deps | Est | Status |
|---|---|---|---|---|---|
''' + "\n".join(rows) + f'''

**{len(tasks)} tasks · {total_s}×S + {total_m}×M + {total_l}×L ≈ {days:g} person-days.**

## Dependency graph

```mermaid
{mermaid.strip()}
```

## DoR / DoD

- **DoR:** the feature's PRD, SAD, data-model and test-plan are accepted
  ([[../../../IMPLEMENTATION-READINESS|readiness]]); the task's own ACs and ADR links resolve.
- **DoD (every task):** {dod}

See [[../../../IMPLEMENTATION-READINESS]] §4 for the full per-task checklist.
''')
    (base / "tracker.md").write_text(content)
