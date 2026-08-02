# T03 — CV upload, text extraction and versioning

**Layer:** app · **Deps:** T02 · **Est:** M · **Owner:** Viacheslav

## What

An owner-scoped upload endpoint: sniff the media type rather than trusting the
extension, cap at 5 MB, extract text in-process, hash the content, and insert an immutable version.
**The binary is discarded after extraction** — less personal data at rest and no file to serve.

## Done when

- Upload, replace and read are owner-scoped; without the scope they are refused (AC-07).
- Media type is sniffed from content; a `.pdf` extension on a ZIP is rejected.
- A file above 5 MB is refused before extraction begins.
- A PDF with no extractable text is refused with a clear message — the system does not OCR.
- Identical content produces no new version (content-hash no-op).
- The uploaded binary is not persisted anywhere — asserted by a filesystem and column scan.
- Extraction runs in-process with no shell-out.

## Links

[[../adr/0002-cv-versioning-and-restaling|ADR-F4-0002]] · [[../../../engineering/security]] §5
