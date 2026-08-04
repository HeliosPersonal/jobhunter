using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The port over apply-link liveness verification at digest assembly (F5 SAD §11 D3, AC-11). Given a card's
/// apply URL, it returns one <see cref="ApplyLinkStatus"/> — reachable, confirmed-unreachable, or unverified
/// — and never throws for an expected outcome: a dead host, a slow host and a robots-disallowed path are all
/// values, because a probe that threw would take the whole digest down with one bad link.
///
/// <para>Defined in Domain so the assembler depends on the verdict, not on <c>HttpClient</c>. The one
/// implementation fetches through the shared politeness-gated client (SAD §8, QG-2), so verification is
/// rate-limited, SSRF-guarded and <c>robots.txt</c>-aware exactly like any other outbound fetch — a robots
/// refusal comes back as <see cref="ApplyLinkStatus.Unverified"/>, never as a circumvention. The probe is
/// bounded by a short timeout (SAD §11 D3: 5 s) so verification never exceeds the assembly window; a timeout
/// is <see cref="ApplyLinkStatus.Unverified"/>, not <see cref="ApplyLinkStatus.ConfirmedUnreachable"/>.</para>
/// </summary>
public interface IApplyLinkVerifier
{
    /// <summary>
    /// Probes <paramref name="applyUrl"/> and classifies its liveness. Returns
    /// <see cref="ApplyLinkStatus.ConfirmedUnreachable"/> only for a definitive 4xx/5xx or a DNS/transport
    /// failure; a timeout, a rate deferral or a robots refusal is <see cref="ApplyLinkStatus.Unverified"/>.
    /// </summary>
    Task<ApplyLinkStatus> VerifyAsync(string applyUrl, CancellationToken cancellationToken = default);
}
