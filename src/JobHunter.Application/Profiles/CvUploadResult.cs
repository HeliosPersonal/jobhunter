namespace JobHunter.Application.Profiles;

/// <summary>
/// The metadata outcome of a CV upload (T03). It carries the version's identity and number and whether a
/// new version was actually created — <see cref="Created"/> is false when identical content was
/// re-uploaded and the existing version was returned unchanged (the content-hash no-op). It deliberately
/// carries <strong>no CV text</strong>: the extracted text crosses exactly one boundary, the match
/// prompt, and never the API response (the CV-leakage invariant).
/// </summary>
public sealed record CvUploadResult(Guid CvVersionId, short Version, bool Created);
