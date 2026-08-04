namespace JobHunter.Application.Reporting;

/// <summary>
/// Tunables for apply-link verification at digest assembly (F5 SAD §11 D3, AC-11). Bound and validated at
/// startup (coding-standards §options). Two numbers bound the probe so it never threatens the assembly
/// window: <see cref="Timeout"/> caps each individual fetch, and <see cref="MaxParallelism"/> caps how many
/// run at once. Both are config-driven so the Owner can trade verification thoroughness against latency
/// without a deploy.
///
/// <para>The timeout is deliberately short (the SAD's 5 s) because a slow apply page is <em>unverified</em>,
/// not unreachable — waiting longer only risks the digest, and a timeout keeps the card anyway. Verification
/// lives in Application because both the assembler (which fans the probes out with bounded parallelism) and
/// the Infrastructure verifier (which enforces the per-probe timeout) read the same values.</para>
/// </summary>
public sealed class ApplyVerificationOptions
{
    public const string SectionName = "ApplyVerification";

    /// <summary>
    /// The per-probe timeout (F5 SAD §11 D3). The default 5 s bounds each apply-link fetch; a probe that
    /// exceeds it is <em>unverified</em>, not confirmed-unreachable, so its card is kept and flagged.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The most apply-link probes that run concurrently (F5 SAD §11 D3). The default 8 keeps verification
    /// inside the assembly window without stampeding the shared politeness gate, which is per-host anyway.
    /// </summary>
    public int MaxParallelism { get; init; } = 8;
}
