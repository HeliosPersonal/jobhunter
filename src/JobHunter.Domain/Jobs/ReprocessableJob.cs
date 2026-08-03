namespace JobHunter.Domain.Jobs;

/// <summary>
/// A flat read model of a job to be recomputed by an offline reprocessing run (AC-09). Carries only what the
/// reprocessor needs to re-normalise from the stored origin payload and decide whether the opening moved: the
/// job's identity, its company, the origin raw posting whose payload is re-parsed, and the fingerprint it
/// currently holds so a changed rule is detected without re-reading the whole aggregate. Quarantined and
/// already-superseded jobs are never returned — reprocessing leaves those terminal states alone.
/// </summary>
public sealed record ReprocessableJob(
    Guid JobId,
    Guid CompanyId,
    Guid OriginRawPostingId,
    string Fingerprint);
