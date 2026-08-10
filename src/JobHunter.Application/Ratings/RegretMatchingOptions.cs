namespace JobHunter.Application.Ratings;

/// <summary>
/// Tunables for the weekly regret match (F4 T21, ADR-F4-0003). The regret sampler scores a fixed, small
/// sample of pre-match-excluded jobs at the cheap tier to falsify the pre-match filter, so — unlike a Run —
/// it has no cost ceiling and no ledger: it is a bounded diagnostic, off the digest's critical path, and its
/// spend is measured and recorded in the ops runbook rather than enforced per call. What <em>is</em> bounded
/// is time: <see cref="Timeout"/> caps the whole submit-and-poll budget so a stuck provider batch can never
/// hang the weekly job, and <see cref="PollInterval"/> spaces the status checks inside it. Bound and
/// validated at startup (coding-standards §options).
/// </summary>
public sealed class RegretMatchingOptions
{
    public const string SectionName = "RegretMatching";

    /// <summary>
    /// The whole regret-match budget — submission plus polling. When it elapses the sample is abandoned and
    /// the matcher returns whatever it has, so a stuck batch never hangs the weekly job. Generous because the
    /// sample runs weekly and off any critical path, unlike the inline narrative note.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromHours(1);

    /// <summary>How long to wait between status polls inside the budget. The default 30 s keeps polling cheap.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);
}
