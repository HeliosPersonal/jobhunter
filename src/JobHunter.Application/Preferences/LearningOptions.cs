namespace JobHunter.Application.Preferences;

/// <summary>
/// The Owner's master switch over preference learning (F7 PRD AC-07, US-04). Bound and validated at startup
/// (coding-standards §options). When <see cref="Enabled"/> is off, the active model is not even consulted:
/// ranking renormalises the preference weight away and orders on match, freshness and the Owner's
/// <em>explicit</em> Profile preferences alone, and the daily digest states that learning is off — so a bad
/// week of inference can be silenced wholesale without deleting a single signal (the evidence survives for
/// when it is turned back on).
///
/// <para>Distinct from a per-weight <c>disabled</c> flag (AC-06), which switches off one learned preference;
/// this is the whole learner. Defaults to on — the feature earns its keep by being on, and the switch is the
/// escape hatch, not the norm.</para>
/// </summary>
public sealed class LearningOptions
{
    public const string SectionName = "Learning";

    /// <summary>
    /// Whether the learned preference model shapes ordering at all. On by default; when off, only explicit
    /// Profile preferences apply and the digest says so (AC-07).
    /// </summary>
    public bool Enabled { get; init; } = true;
}
