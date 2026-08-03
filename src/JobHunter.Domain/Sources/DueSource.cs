namespace JobHunter.Domain.Sources;

/// <summary>
/// One source the discovery cycle should fan out this window (SAD §6.1): an active company with a live,
/// confident (≥ 0.80), non-quarantined binding that has not already been fetched in this window. Flat by
/// design — the cycle publishes one <c>SourceFetchRequested</c> per row and nothing more, so the read
/// carries only what the message needs. <see cref="AtsKind"/> is the persisted <c>text</c> value.
///
/// <para><see cref="CompBand"/> and <see cref="RemoteEmeaFriendly"/> are the curated comp-and-remote
/// segmentation (T15). They carry no filtering power here — every due source still fans out — but they let
/// the cycle order the fan-out toward the Owner's target band, reason-visible (TUNE-10). Null when the
/// company is untagged.</para>
/// </summary>
public sealed record DueSource(
    Guid SourceId,
    Guid CompanyId,
    string AtsKind,
    string? CompBand = null,
    bool? RemoteEmeaFriendly = null);
