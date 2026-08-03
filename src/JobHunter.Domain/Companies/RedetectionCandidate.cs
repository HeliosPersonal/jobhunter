namespace JobHunter.Domain.Companies;

/// <summary>
/// One company the weekly re-detection should probe this run (SAD §6.2, AC-05): a company whose live
/// binding is older than the staleness cutoff, or whose board returned zero postings on its last two
/// successful cycles. Flat by design — the handler loads the full <see cref="Company"/> and probes it, so
/// the read carries only the key.
/// </summary>
public sealed record RedetectionCandidate(Guid CompanyId);
