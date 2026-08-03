using JobHunter.Application.Normalization;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Postings;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Deduplication;

/// <summary>
/// Stage 3: turns a candidate job into the canonical one, or folds it into an existing opening as an alias
/// (SAD §6.1). It re-runs the deterministic normalisation over the stored payload — the candidate id and
/// fingerprint come out byte-identical to what <c>NormalizationHandler</c> published — then attempts the
/// conflict-tolerant insert. The insert is the whole concurrency design: <c>ON CONFLICT (fingerprint) DO
/// NOTHING</c> means two consumers racing on one opening produce exactly one job and one alias each, with
/// no lock and no read-then-write (invariant 2, ADR-F2-0001).
///
/// <para>On a genuine insert it publishes <see cref="JobDiscovered"/>; on a fingerprint conflict it loads
/// the winning job, registers this posting as an alias — bumping <c>last_seen_at</c> — and publishes
/// <see cref="JobDuplicateDetected"/>. Idempotent on the fingerprint: a replayed <see cref="JobNormalized"/>
/// conflicts and re-registers the same alias (itself idempotent per raw posting), so a replay never forks a
/// second job nor double-counts an alias. A vanished posting, an unroutable provider or a missing company is
/// a recorded (logged) failure that ends the message cleanly, never a throw that poisons the batch (AC-04).</para>
/// </summary>
public sealed class DeduplicationHandler(
    IRawPostingReader rawPostings,
    IJobSourceRepository sources,
    ICompanyRepository companies,
    IPostingNormalizerCatalog normalizers,
    TechnologyTagger technologyTagger,
    IJobRepository jobs,
    IClock clock,
    ILogger<DeduplicationHandler> logger)
{
    private readonly IRawPostingReader _rawPostings = rawPostings ?? throw new ArgumentNullException(nameof(rawPostings));
    private readonly IJobSourceRepository _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly ICompanyRepository _companies = companies ?? throw new ArgumentNullException(nameof(companies));
    private readonly IPostingNormalizerCatalog _normalizers = normalizers ?? throw new ArgumentNullException(nameof(normalizers));
    private readonly TechnologyTagger _technologyTagger = technologyTagger ?? throw new ArgumentNullException(nameof(technologyTagger));
    private readonly IJobRepository _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<DeduplicationHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(
        JobNormalized message,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var raw = await _rawPostings.FindAsync(message.RawPostingId, cancellationToken).ConfigureAwait(false);
        if (raw is null)
        {
            _logger.LogWarning(
                "Raw posting {RawPostingId} no longer exists; skipping deduplication.", message.RawPostingId);
            return;
        }

        var source = await _sources.FindAsync(raw.SourceId, cancellationToken).ConfigureAwait(false);
        var binding = source is null
            ? null
            : await _companies.FindBindingAsync(source.BindingId, cancellationToken).ConfigureAwait(false);
        if (binding is null)
        {
            _logger.LogWarning(
                "Raw posting {RawPostingId} has no resolvable binding; skipping deduplication.",
                message.RawPostingId);
            return;
        }

        var normalizer = _normalizers.For(binding.AtsKind);
        if (normalizer is null)
        {
            _logger.LogWarning(
                "No normaliser registered for ATS kind {AtsKind}; raw posting {RawPostingId} is unroutable.",
                binding.AtsKind, message.RawPostingId);
            return;
        }

        var company = await _companies.FindAsync(message.CompanyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            _logger.LogWarning(
                "Company {CompanyId} for raw posting {RawPostingId} not found; skipping deduplication.",
                message.CompanyId, message.RawPostingId);
            return;
        }

        var extraction = normalizer.Extract(raw.Payload);
        if (extraction.IsFailure)
        {
            _logger.LogWarning(
                "Re-normalisation of raw posting {RawPostingId} ({AtsKind}) failed: {Reason}.",
                message.RawPostingId, binding.AtsKind, extraction.Error.Code);
            return;
        }

        var context = new NormalizationContext(
            message.CompanyId,
            message.RawPostingId,
            raw.SourceId,
            company.CanonicalDomain.Value,
            raw.FetchedAt,
            raw.LastSeenAt);

        var candidate = CandidateJobFactory.Create(message.JobId, extraction.Value, context, _technologyTagger);
        if (candidate.IsFailure)
        {
            _logger.LogWarning(
                "Raw posting {RawPostingId} ({AtsKind}) is missing a required field: {Reason}.",
                message.RawPostingId, binding.AtsKind, candidate.Error.Code);
            return;
        }

        var job = candidate.Value;

        var outcome = await _jobs.InsertAsync(job, cancellationToken).ConfigureAwait(false);
        if (outcome == JobInsertOutcome.Inserted)
        {
            await bus.PublishAsync(new JobDiscovered(
                job.Id, job.CompanyId, job.Title, job.FirstSeenAt, _clock.UtcNow)).ConfigureAwait(false);

            _logger.LogInformation(
                "Discovered new job {JobId} for company {CompanyId} from raw posting {RawPostingId}.",
                job.Id, job.CompanyId, message.RawPostingId);
            return;
        }

        await RecordAliasAsync(message, job, raw, bus, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordAliasAsync(
        JobNormalized message,
        Domain.Jobs.Job candidate,
        RawPostingContent raw,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var canonical = await _jobs.FindByFingerprintAsync(candidate.Fingerprint, cancellationToken).ConfigureAwait(false);
        if (canonical is null)
        {
            // The conflicting row vanished between the insert and the load (a closure prune, say). There is
            // nothing to alias onto; record it and end cleanly rather than throw into the batch.
            _logger.LogWarning(
                "Fingerprint conflict for raw posting {RawPostingId} but the canonical job was not found; skipping.",
                message.RawPostingId);
            return;
        }

        canonical.RegisterAlias(message.RawPostingId, raw.SourceId, raw.FetchedAt, raw.LastSeenAt);
        await _jobs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await bus.PublishAsync(new JobDuplicateDetected(
            canonical.Id, message.RawPostingId, raw.SourceId, _clock.UtcNow)).ConfigureAwait(false);

        _logger.LogInformation(
            "Raw posting {RawPostingId} is a duplicate of job {JobId}; recorded as an alias.",
            message.RawPostingId, canonical.Id);
    }
}
