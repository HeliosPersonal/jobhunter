using System.Globalization;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;

namespace JobHunter.Application.Matching;

/// <summary>
/// The pre-match filter (ADR-F4-0003, T12): the <em>factual</em> gate a job clears before the expensive deep
/// tier judges it against the CV. Like <see cref="Ranking.ScoreCalculator"/> it is a <strong>static, pure</strong>
/// function of explicit values — no clock, no repository, no CV text — which is what lets its outcome be proven
/// against the labelled reference corpus with no LLM in the loop (test-plan §The pre-match reference corpus).
///
/// <para>Every rule is a <em>fact</em> drawn from the job's enrichment, the job's own posting facts and the
/// active <see cref="Profile"/>; never a judgement. "Below the stated salary floor at high confidence" is a
/// fact. "Probably a weak culture fit" is a judgement and belongs in the deep tier. The five rules, evaluated
/// in the fixed order of the ADR table so a corpus case that fails exactly one rule is attributed to it:</para>
/// <list type="number">
/// <item><description><b>Timezone</b> — the job's band is a definite region incompatible with the Owner's
/// band <em>and</em> the role is not remote. A job whose band is <see cref="TimezoneBand.Global"/> or
/// <see cref="TimezoneBand.Unknown"/> is never excluded on this ground: incompatibility must be a fact.</description></item>
/// <item><description><b>Employment type</b> — the job's stated engagement is a recognised type the Owner does
/// not seek. An <see cref="EmploymentType.Unknown"/> posting is never excluded here — an unstated type is not
/// a factual mismatch.</description></item>
/// <item><description><b>Seniority floor</b> — the job's level is <c>gap</c> or more rungs below the Owner's on
/// the individual-contributor ladder. Off-ladder levels (<see cref="Seniority.Lead"/>, <see cref="Seniority.Manager"/>)
/// and untitled postings are never excluded — a level that cannot be compared is not a factual mismatch. Nor is
/// an early-stage role (a configured <see cref="CompanyStage"/>, <c>{Seed, SeriesA}</c> by default), whose
/// erratic levelling makes the absolute gap unreliable — the floor is lifted, but every other rule still
/// applies (T18).</description></item>
/// <item><description><b>Salary</b> — the enrichment's estimated pay sits entirely below the Owner's floor, in
/// the same currency, at a confidence at or above the threshold. Cross-currency and low-confidence estimates
/// never bite: the floor rule refuses to guess.</description></item>
/// <item><description><b>Lifecycle</b> — the job already carries a current match against the active CV version,
/// so re-judging it would only repay for a conclusion already reached. Closure is handled upstream by the
/// scope query, which returns only <c>Live</c> jobs.</description></item>
/// </list>
///
/// <para>The bypass (<c>Run:MatchAllJobs</c>) is a caller concern, not a rule: the submission handler simply
/// skips the filter for a calibration Run. This keeps the filter a total function of its inputs.</para>
/// </summary>
public static class PreMatchFilter
{
    /// <summary>
    /// Decides whether <paramref name="job"/> clears the factual gate. <paramref name="hasCurrentMatch"/> is
    /// the lifecycle input the caller reads from the match store — the filter never touches <c>matches</c>
    /// itself (architecture rule: the filter reads only enrichment, job facts and the Profile).
    /// <paramref name="settings"/> carries the Owner's seniority and the tunable thresholds. Returns a passing
    /// verdict, or an excluding one naming the single rule that disqualified the job (invariant 11, AC-12).
    /// </summary>
    public static PreMatchVerdict Evaluate(
        MatchJobContent job,
        Profile profile,
        bool hasCurrentMatch,
        PreMatchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(profile);

        if (TimezoneIncompatible(job, profile, out var timezoneReason))
        {
            return PreMatchVerdict.Exclude(PreMatchRule.Timezone, timezoneReason);
        }

        if (EmploymentTypeNotSought(job, profile, out var employmentReason))
        {
            return PreMatchVerdict.Exclude(PreMatchRule.EmploymentType, employmentReason);
        }

        if (BelowSeniorityFloor(job, settings, out var seniorityReason))
        {
            return PreMatchVerdict.Exclude(PreMatchRule.SeniorityFloor, seniorityReason);
        }

        if (BelowSalaryFloor(job, profile, settings, out var salaryReason))
        {
            return PreMatchVerdict.Exclude(PreMatchRule.SalaryFloor, salaryReason);
        }

        if (hasCurrentMatch)
        {
            return PreMatchVerdict.Exclude(
                PreMatchRule.Lifecycle, "Already matched against the current CV version");
        }

        return PreMatchVerdict.Pass;
    }

    private static bool TimezoneIncompatible(MatchJobContent job, Profile profile, out string reason)
    {
        reason = string.Empty;

        // No enrichment means no timezone fact to act on, and a remote role overrides the band entirely.
        if (job.Enrichment is not { } enrichment || enrichment.IsRemote)
        {
            return false;
        }

        // Incompatibility must be a fact: only two definite, differing regions disqualify. Global overlaps
        // every band, and Unknown is the absence of a fact, so neither ever excludes.
        if (!IsDefiniteRegion(enrichment.TimezoneBand) || !IsDefiniteRegion(profile.TimezoneBand))
        {
            return false;
        }

        if (enrichment.TimezoneBand == profile.TimezoneBand)
        {
            return false;
        }

        reason = string.Create(
            CultureInfo.InvariantCulture,
            $"Timezone band {enrichment.TimezoneBand} incompatible with {profile.TimezoneBand} and not remote");
        return true;
    }

    private static bool EmploymentTypeNotSought(MatchJobContent job, Profile profile, out string reason)
    {
        reason = string.Empty;

        // An unrecognised or unstated type is not a factual mismatch — only a known type the Owner does not seek is.
        if (!Enum.TryParse<EmploymentType>(job.EmploymentType, ignoreCase: false, out var type)
            || type == EmploymentType.Unknown)
        {
            return false;
        }

        if (profile.EmploymentTypes.Contains(type))
        {
            return false;
        }

        reason = string.Create(
            CultureInfo.InvariantCulture, $"Employment type {type} not among the sought types");
        return true;
    }

    private static bool BelowSeniorityFloor(MatchJobContent job, PreMatchSettings settings, out string reason)
    {
        reason = string.Empty;

        // T18: an early-stage role (Seed / Series A by default) is exempt from the floor entirely — startup
        // levelling is erratic enough that an absolute level gap is not a fact worth dropping a wanted
        // Founding-Engineer role on. The exemption is evidence-driven: it needs an enrichment stage fact, so a
        // job with no enrichment can never claim it. Only the floor is lifted; every other rule still applies.
        if (job.Enrichment is { } enrichment
            && settings.SeniorityFloorExemptStages.Contains(enrichment.CompanyStage))
        {
            return false;
        }

        // Only a comparable IC level, a rung on the same ladder as the Owner's, can be a factual floor breach.
        if (!Enum.TryParse<Seniority>(job.Seniority, ignoreCase: false, out var jobSeniority))
        {
            return false;
        }

        if (SeniorityLadder.Rung(jobSeniority) is not { } jobRung
            || SeniorityLadder.Rung(settings.OwnerSeniority) is not { } ownerRung)
        {
            return false;
        }

        if (ownerRung - jobRung < settings.SeniorityFloorGap)
        {
            return false;
        }

        reason = string.Create(
            CultureInfo.InvariantCulture,
            $"Seniority {jobSeniority} is {ownerRung - jobRung} levels below {settings.OwnerSeniority}");
        return true;
    }

    private static bool BelowSalaryFloor(
        MatchJobContent job, Profile profile, PreMatchSettings settings, out string reason)
    {
        reason = string.Empty;

        if (profile.SalaryFloor is not { } floor || profile.SalaryFloorCurrency is not { } floorCurrency)
        {
            return false;
        }

        if (job.Enrichment?.EstimatedSalary is not { } estimate)
        {
            return false;
        }

        // Never compare across currencies — a euros-versus-dollars answer would be a lie (SalaryEstimate).
        if (!string.Equals(estimate.Currency, floorCurrency, StringComparison.Ordinal))
        {
            return false;
        }

        if (estimate.Confidence < settings.SalaryConfidenceThreshold)
        {
            return false;
        }

        // The whole estimated range sits below the floor: even the top of the band misses it.
        if (estimate.Max >= floor)
        {
            return false;
        }

        reason = string.Create(
            CultureInfo.InvariantCulture,
            $"Estimated salary below floor ({floorCurrency} {floor:0.##}) at confidence {estimate.Confidence:0.00}");
        return true;
    }

    private static bool IsDefiniteRegion(TimezoneBand band) =>
        band is TimezoneBand.EMEA or TimezoneBand.AMER or TimezoneBand.APAC;
}
