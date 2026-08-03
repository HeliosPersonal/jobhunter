using JobHunter.Application.Deduplication;
using JobHunter.Application.Normalization;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Reprocessing;

/// <summary>
/// Recomputes canonical jobs from their stored raw payloads (AC-09, QG-3) — the offline half of the F2
/// pipeline. An improved normalisation rule is applied to history without contacting any provider: this
/// service re-reads each job's origin payload through <see cref="IRawPostingReader"/>, re-runs the same pure
/// <see cref="CandidateJobFactory"/> the live pipeline uses, and compares the recomputed fingerprint with the
/// one the job currently holds.
///
/// <para><strong>Zero network is structural.</strong> The service depends on the stored-payload reader and
/// the job repository — never on <see cref="IJobSource"/> — so a run physically cannot fetch (QG-3). When the
/// fingerprint is unchanged the job is left exactly as it is, so its enrichments and matches stay attached
/// (AC-09). When the fingerprint changed the opening has moved: a new job is inserted for the new fingerprint
/// (or folded onto an existing one on a conflict, a genuine merge under the improved rule) and the old job is
/// recorded as <see cref="JobStatus.Superseded"/> pointing at its successor — retained, not orphaned. A
/// vanished or unparseable stored payload is a recorded failure that is counted and skipped, never a throw
/// that halts the run (AC-04).</para>
/// </summary>
public sealed class ReprocessingService(
    IReprocessableJobsQuery reprocessable,
    IRawPostingReader rawPostings,
    IJobSourceRepository sources,
    ICompanyRepository companies,
    IPostingNormalizerCatalog normalizers,
    TechnologyTagger technologyTagger,
    IJobRepository jobs,
    IIdGenerator ids,
    IClock clock,
    ILogger<ReprocessingService> logger)
{
    private readonly IReprocessableJobsQuery _reprocessable = reprocessable ?? throw new ArgumentNullException(nameof(reprocessable));
    private readonly IRawPostingReader _rawPostings = rawPostings ?? throw new ArgumentNullException(nameof(rawPostings));
    private readonly IJobSourceRepository _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly ICompanyRepository _companies = companies ?? throw new ArgumentNullException(nameof(companies));
    private readonly IPostingNormalizerCatalog _normalizers = normalizers ?? throw new ArgumentNullException(nameof(normalizers));
    private readonly TechnologyTagger _technologyTagger = technologyTagger ?? throw new ArgumentNullException(nameof(technologyTagger));
    private readonly IJobRepository _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<ReprocessingService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Reprocesses every job first seen at or after <paramref name="firstSeenFrom"/> and returns the tally.
    /// </summary>
    public async Task<ReprocessingReport> ReprocessAsync(
        DateTimeOffset firstSeenFrom,
        CancellationToken cancellationToken)
    {
        var examined = 0;
        var unchanged = 0;
        var superseded = 0;
        var failed = 0;

        await foreach (var candidate in _reprocessable.StreamAsync(firstSeenFrom, cancellationToken).ConfigureAwait(false))
        {
            examined++;

            var recomputed = await RecomputeAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (recomputed.IsFailure)
            {
                failed++;
                continue;
            }

            var job = recomputed.Value;
            if (string.Equals(job.Fingerprint.Value, candidate.Fingerprint, StringComparison.Ordinal))
            {
                // The improved rule produced the same fingerprint: the identity is stable, so the job — and
                // everything attached to it — is left untouched (AC-09).
                unchanged++;
                continue;
            }

            if (await SupersedeAsync(candidate, job, cancellationToken).ConfigureAwait(false))
            {
                superseded++;
            }
            else
            {
                failed++;
            }
        }

        _logger.LogInformation(
            "Reprocessing complete from {FirstSeenFrom:o}: {Examined} examined, {Unchanged} unchanged, " +
            "{Superseded} superseded, {Failed} failed.",
            firstSeenFrom, examined, unchanged, superseded, failed);

        return new ReprocessingReport(examined, unchanged, superseded, failed);
    }

    /// <summary>
    /// Re-builds the candidate job from the stored origin payload with a fresh id. A missing/unroutable/
    /// unparseable payload is a failure the caller counts and skips (AC-04).
    /// </summary>
    private async Task<Domain.Common.Result<Job>> RecomputeAsync(
        ReprocessableJob candidate,
        CancellationToken cancellationToken)
    {
        var raw = await _rawPostings.FindAsync(candidate.OriginRawPostingId, cancellationToken).ConfigureAwait(false);
        if (raw is null)
        {
            _logger.LogWarning(
                "Origin raw posting {RawPostingId} for job {JobId} no longer exists; skipping.",
                candidate.OriginRawPostingId, candidate.JobId);
            return RecomputeFailed;
        }

        var source = await _sources.FindAsync(raw.SourceId, cancellationToken).ConfigureAwait(false);
        var binding = source is null
            ? null
            : await _companies.FindBindingAsync(source.BindingId, cancellationToken).ConfigureAwait(false);
        if (binding is null)
        {
            _logger.LogWarning(
                "Job {JobId} has no resolvable binding for its origin posting; skipping.", candidate.JobId);
            return RecomputeFailed;
        }

        var normalizer = _normalizers.For(binding.AtsKind);
        if (normalizer is null)
        {
            _logger.LogWarning(
                "No normaliser registered for ATS kind {AtsKind}; job {JobId} is unroutable.",
                binding.AtsKind, candidate.JobId);
            return RecomputeFailed;
        }

        var company = await _companies.FindAsync(candidate.CompanyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            _logger.LogWarning("Company {CompanyId} for job {JobId} not found; skipping.", candidate.CompanyId, candidate.JobId);
            return RecomputeFailed;
        }

        var extraction = normalizer.Extract(raw.Payload);
        if (extraction.IsFailure)
        {
            _logger.LogWarning(
                "Re-normalisation of job {JobId} ({AtsKind}) failed: {Reason}.",
                candidate.JobId, binding.AtsKind, extraction.Error.Code);
            return RecomputeFailed;
        }

        var context = new NormalizationContext(
            candidate.CompanyId,
            candidate.OriginRawPostingId,
            raw.SourceId,
            company.CanonicalDomain.Value,
            raw.FetchedAt,
            raw.LastSeenAt);

        var rebuilt = CandidateJobFactory.Create(_ids.NewId(), extraction.Value, context, _technologyTagger);
        if (rebuilt.IsFailure)
        {
            _logger.LogWarning(
                "Job {JobId} ({AtsKind}) is now missing a required field: {Reason}.",
                candidate.JobId, binding.AtsKind, rebuilt.Error.Code);
        }

        return rebuilt;
    }

    /// <summary>
    /// Persists the moved opening: insert the recomputed job (or fold it onto the existing holder of the new
    /// fingerprint on a conflict), then retire the old job in favour of the successor. Returns false when the
    /// old job vanished or the successor could not be resolved, so the caller counts it as a failure.
    /// </summary>
    private async Task<bool> SupersedeAsync(
        ReprocessableJob candidate,
        Job recomputed,
        CancellationToken cancellationToken)
    {
        var outcome = await _jobs.InsertAsync(recomputed, cancellationToken).ConfigureAwait(false);

        var successor = outcome == JobInsertOutcome.Inserted
            ? recomputed
            : await _jobs.FindByFingerprintAsync(recomputed.Fingerprint, cancellationToken).ConfigureAwait(false);
        if (successor is null)
        {
            _logger.LogWarning(
                "Reprocessed job {JobId} changed fingerprint but no successor could be resolved; skipping.",
                candidate.JobId);
            return false;
        }

        var old = await _jobs.FindAsync(candidate.JobId, cancellationToken).ConfigureAwait(false);
        if (old is null)
        {
            _logger.LogWarning(
                "Reprocessed job {JobId} vanished before it could be superseded; skipping.", candidate.JobId);
            return false;
        }

        var result = old.Supersede(successor.Id, _clock.UtcNow);
        if (result.IsFailure)
        {
            // A quarantined job is deliberately left withheld — never superseded out from under review.
            _logger.LogInformation(
                "Job {JobId} was not superseded ({Reason}); left as-is.", candidate.JobId, result.Error.Code);
            return false;
        }

        await _jobs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Job {OldJobId} superseded by {NewJobId} after a reprocessing fingerprint change.",
            candidate.JobId, successor.Id);
        return true;
    }

    private static readonly Domain.Common.Error RecomputeError =
        new("job.reprocess.recompute_failed", "The stored payload could not be recomputed.");

    private static Domain.Common.Result<Job> RecomputeFailed => Domain.Common.Result<Job>.Failure(RecomputeError);
}
