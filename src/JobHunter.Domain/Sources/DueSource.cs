namespace JobHunter.Domain.Sources;

/// <summary>
/// One source the discovery cycle should fan out this window (SAD §6.1): an active company with a live,
/// confident (≥ 0.80), non-quarantined binding that has not already been fetched in this window. Flat by
/// design — the cycle publishes one <c>SourceFetchRequested</c> per row and nothing more, so the read
/// carries only what the message needs. <see cref="AtsKind"/> is the persisted <c>text</c> value.
/// </summary>
public sealed record DueSource(Guid SourceId, Guid CompanyId, string AtsKind);
