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

## Links

[[../contracts/command-catalogue|catalogue]] §Pipeline, §Company · [[../../f6-application-tracking/index|F6]] · [[../../f8-company-research-agent/index|F8]]
