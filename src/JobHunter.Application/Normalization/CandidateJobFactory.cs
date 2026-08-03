using JobHunter.Application.Deduplication;
using JobHunter.Domain.Common;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Assembles a candidate <see cref="Job"/> from an <see cref="ExtractedPosting"/> and its
/// <see cref="NormalizationContext"/> (SAD §6.1). This is the shared, provider-agnostic core: it runs the
/// title, location, remote-policy and salary normalisers, computes the fingerprint (ADR-F2-0001) and stamps
/// the origin alias, so the same posting always becomes the same job. It is a <strong>pure function</strong>
/// of its inputs (SAD S5) — no clock, no randomness, no I/O — which is what makes reprocessing (QG-3) free:
/// the reprocessor calls it again over stored payloads and gets a byte-identical fingerprint.
///
/// <para>A missing required field (title, apply URL) is a <see cref="Result{T}"/> failure, never an
/// exception, so one bad posting is recorded and skipped without halting the batch (AC-04). The published
/// title is preserved untouched; only the never-displayed normalised form feeds the fingerprint (AC-05).</para>
/// </summary>
public static class CandidateJobFactory
{
    public static readonly Error MissingTitle =
        new("job.normalize.missing_title", "A posting with no title cannot become a job.");

    public static readonly Error MissingApplyUrl =
        new("job.normalize.missing_apply_url", "A posting with no apply URL cannot become a job.");

    /// <summary>
    /// Builds the candidate job with id <paramref name="jobId"/>. Returns a failure when a required field is
    /// absent (AC-04). The origin posting is registered as the job's first alias, so every job carries at
    /// least one alias including its creator (AC-08). When a <paramref name="tagger"/> is supplied, the job
    /// is also tagged with the deterministic vocabulary technologies found in its title and description
    /// (T07); passing none leaves the job untagged, which keeps the factory a pure function of its inputs
    /// for the cases (and tests) that do not need tags.
    /// </summary>
    public static Result<Job> Create(
        Guid jobId,
        ExtractedPosting posting,
        NormalizationContext context,
        TechnologyTagger? tagger = null)
    {
        ArgumentNullException.ThrowIfNull(posting);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(posting.Title))
        {
            return MissingTitle;
        }

        if (string.IsNullOrWhiteSpace(posting.ApplyUrl))
        {
            return MissingApplyUrl;
        }

        var normalisedTitle = TitleNormalizer.Normalize(posting.Title);

        var locations = posting.Locations
            ?? LocationParser.Parse(posting.LocationText);

        var remotePolicy = RemotePolicyResolver.Resolve(posting.RemoteSignal, posting.LocationText);

        var salary = SalaryParser.Parse(posting.SalaryText, posting.SalaryDefaultPeriod);

        var fingerprint = FingerprintCalculator.Compute(
            context.CanonicalDomain, normalisedTitle.Value, locations);

        var job = new Job(
            jobId,
            context.CompanyId,
            context.RawPostingId,
            fingerprint,
            FingerprintCalculator.Version,
            posting.Title,
            normalisedTitle.Value,
            posting.Description,
            posting.ApplyUrl,
            locations,
            remotePolicy,
            posting.EmploymentType,
            posting.PostedAtGranularity,
            context.FirstSeenAt,
            context.LastSeenAt,
            normalisedTitle.Seniority,
            salary.Range,
            salary.Raw,
            posting.PostedAt,
            posting.IsTier2);

        job.RegisterAlias(context.RawPostingId, context.SourceId, context.FirstSeenAt, context.LastSeenAt);

        tagger?.Tag(job);

        return job;
    }
}
