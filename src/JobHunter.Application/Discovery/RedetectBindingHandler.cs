using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Discovery;

/// <summary>
/// Weekly binding re-detection and ATS migration (SAD §6.2, AC-05). It probes the companies due this run —
/// a live binding older than <see cref="DiscoveryOptions.BindingMaxAge"/>, or a board that returned zero
/// postings on its last <see cref="DiscoveryOptions.RedetectionEmptyCycles"/> successful cycles — and only
/// the day's bucket, so the weekly re-probe is spread across the week rather than stampeding on one day.
///
/// When a company has migrated provider — detection resolves a binding whose provider or token differs from
/// the current live one — the old binding is <see cref="AtsBinding.Retire">retired, never deleted</see> (so
/// the migration is auditable), the new binding is recorded, and the operational source is
/// <see cref="JobSource.RebindTo">re-pointed</see> at it. The source keeps its company id, so every posting
/// already discovered under the old binding stays attached to the same company — the key is the company,
/// not the board token. A re-detection that confirms the same provider only refreshes the binding so it is
/// no longer stale; an ambiguous or empty probe leaves the company untouched, so a board that legitimately
/// has no openings is never retired on that basis.
/// </summary>
public sealed class RedetectBindingHandler(
    IRedetectionQuery dueCandidates,
    ICompanyRepository companies,
    IJobSourceRepository sources,
    IBindingDetector detector,
    IIdGenerator ids,
    IClock clock,
    ILogger<RedetectBindingHandler> logger)
{
    private readonly IRedetectionQuery _dueCandidates = dueCandidates ?? throw new ArgumentNullException(nameof(dueCandidates));
    private readonly ICompanyRepository _companies = companies ?? throw new ArgumentNullException(nameof(companies));
    private readonly IJobSourceRepository _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly IBindingDetector _detector = detector ?? throw new ArgumentNullException(nameof(detector));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<RedetectBindingHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(RedetectBindingsDue message, DiscoveryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);

        var staleBefore = message.WindowStart - options.BindingMaxAge;
        var dayBucket = DayBucket(message.WindowStart, options.RedetectionBuckets);

        var candidates = await _dueCandidates.DueCandidatesAsync(
            staleBefore, options.RedetectionEmptyCycles, dayBucket, options.RedetectionBuckets, cancellationToken)
            .ConfigureAwait(false);

        var migrated = 0;
        var reconfirmed = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var company = await _companies.FindAsync(candidate.CompanyId, cancellationToken).ConfigureAwait(false);
            if (company is null)
            {
                continue;
            }

            var detection = await _detector.DetectAsync(company, cancellationToken).ConfigureAwait(false);
            if (detection.Status != BindingDetectionStatus.Bound || detection.Binding is null)
            {
                // NoBoardFound or Ambiguous: leave the company exactly as it is (AC-04). A board that
                // legitimately has no openings must never be retired on a re-detection that found nothing.
                _logger.LogInformation(
                    "Re-detection for company {CompanyId} resolved {Status}; binding left unchanged.",
                    candidate.CompanyId, detection.Status);
                continue;
            }

            var detected = detection.Binding;
            var live = await _companies.LiveBindingsAsync(candidate.CompanyId, cancellationToken).ConfigureAwait(false);
            var current = live.FirstOrDefault(b => b.AtsKind == detected.AtsKind && b.BoardToken == detected.BoardToken);

            if (current is not null)
            {
                // Same provider and token still wins: refresh the binding so it is no longer stale.
                current.Reconfirm(_clock, detected.Evidence);
                reconfirmed++;
                continue;
            }

            await MigrateAsync(candidate.CompanyId, live, detected, cancellationToken).ConfigureAwait(false);
            migrated++;
        }

        if (candidates.Count > 0)
        {
            await _companies.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _sources.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Re-detection run for window {WindowStart:o} bucket {Bucket}: {Probed} probed, {Migrated} migrated, {Reconfirmed} re-confirmed.",
            message.WindowStart, dayBucket, candidates.Count, migrated, reconfirmed);
    }

    private async Task MigrateAsync(
        Guid companyId,
        IReadOnlyList<AtsBinding> liveBindings,
        AtsBinding detected,
        CancellationToken cancellationToken)
    {
        // Retire every currently live binding (never delete — the migration must stay auditable, AC-05).
        foreach (var binding in liveBindings)
        {
            binding.Retire(_clock);
        }

        // Record the new binding with a fresh id and the current instant, keeping the probe evidence.
        var newBinding = new AtsBinding(
            _ids.NewId(), companyId, detected.AtsKind, detected.BoardToken,
            detected.Confidence, detected.Evidence, _clock.UtcNow);
        await _companies.AddBindingAsync(newBinding, cancellationToken).ConfigureAwait(false);

        var endpoint = AtsEndpoint.For(detected.AtsKind, detected.BoardToken);

        // Re-point the existing source at the new binding so previously discovered postings — which reference
        // the source, whose company id is unchanged — stay attached to the same company. If the company had
        // no operational source yet (a re-detected but never-sourced company), create one.
        var source = liveBindings.Count > 0
            ? await FindSourceForAnyBindingAsync(liveBindings, cancellationToken).ConfigureAwait(false)
            : null;

        if (source is null)
        {
            var created = new JobSource(_ids.NewId(), companyId, newBinding.Id, endpoint);
            await _sources.AddAsync(created, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            source.RebindTo(newBinding.Id, endpoint, _clock);
        }

        _logger.LogInformation(
            "Company {CompanyId} migrated to {AtsKind}; previous binding(s) retired, jobs kept.",
            companyId, detected.AtsKind);
    }

    private async Task<JobSource?> FindSourceForAnyBindingAsync(
        IReadOnlyList<AtsBinding> bindings,
        CancellationToken cancellationToken)
    {
        foreach (var binding in bindings)
        {
            var source = await _sources.FindByBindingAsync(binding.Id, cancellationToken).ConfigureAwait(false);
            if (source is not null)
            {
                return source;
            }
        }

        return null;
    }

    /// <summary>
    /// The stable weekly bucket for a run: the day the window falls on, 0..(<paramref name="bucketCount"/>-1).
    /// Companies are hashed into the same buckets by the read query, so a company is probed on one day of the
    /// week — spreading the weekly re-probe rather than running every company on the same day (AC-05).
    /// </summary>
    private static int DayBucket(DateTimeOffset windowStart, int bucketCount) =>
        bucketCount <= 0 ? 0 : ((windowStart.DayOfYear - 1) % bucketCount + bucketCount) % bucketCount;
}
