namespace JobHunter.Domain.Reporting;

/// <summary>
/// The verdict of one apply-link verification at digest assembly (F5 SAD §11 D3, AC-11). A value, not an
/// exception: verification is a best-effort liveness probe whose every outcome is expected, and the card's
/// fate is decided by which of the three it is — never by a thrown fault.
///
/// <para>The distinction the feature turns on is that a <see cref="Unverified"/> result is <em>not</em> a
/// closed job: a slow host or a <c>robots.txt</c>-disallowed path tells us nothing about whether the opening
/// is still open, so the card is kept and flagged rather than dropped. Only a <see cref="ConfirmedUnreachable"/>
/// link — a definitive 4xx/5xx or a DNS/transport failure — drops the card and flags the job for closure.
/// A timeout is <see cref="Unverified"/>, never <see cref="ConfirmedUnreachable"/> (D3).</para>
/// </summary>
public enum ApplyLinkStatus
{
    /// <summary>The apply destination answered with a success status — the card is presented as actionable.</summary>
    Reachable,

    /// <summary>
    /// A definitive 4xx/5xx response or a DNS/transport failure: the destination is gone. The card is
    /// dropped (AC-11) and the job is flagged for the lifecycle sweep so F2 can close it.
    /// </summary>
    ConfirmedUnreachable,

    /// <summary>
    /// The probe was inconclusive — a timeout, a rate deferral, or a <c>robots.txt</c>-disallowed path. The
    /// opening may well be open, so the card is kept and rendered with the "link unverified" flag.
    /// </summary>
    Unverified,
}
