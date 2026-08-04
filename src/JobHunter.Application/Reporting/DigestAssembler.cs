using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Sources;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Reporting;

/// <summary>
/// The digest-assembly step (F5 SAD §6.1). Consumes <see cref="RankingCompleted"/>: it loads the Run's scored
/// candidates (each joined to its current-match reasons and USD salary), selects the cards (score at or above
/// the threshold, capped at the configured maximum, each snapshotting its score and reasons), builds the
/// suppression breakdown, gathers the carried-over and degraded-source counts, persists the whole digest
/// <em>before</em> anything is sent (S2), and publishes <see cref="DigestReady"/>.
///
/// <para>Every promise the feature makes is a property of this assembly. A card cannot exist without a reason,
/// so an unexplained job never reaches the Owner (invariant 4, AC-02) — a reason-less candidate is simply not
/// carded. The suppressed count reconciles to its breakdown by construction (invariant 11, AC-07). The card's
/// score and reasons are copied here, so a later re-score cannot change a delivered digest (QG-3). The average
/// salary is null unless at least a few jobs carry a USD figure, because a mean of one or two is more
/// misleading than absent. And the digest is persisted before <see cref="DigestReady"/> is published, so a
/// consumer that reacts to the event always finds the stored artifact rather than a half-built one.</para>
///
/// <para>Idempotent: one digest per Run is a database constraint (<c>uq_digests_run</c>), so a replayed
/// <see cref="RankingCompleted"/> finds the existing digest, re-emits <see cref="DigestReady"/> for it and
/// writes nothing new. The narrative is a template placeholder here; F5-T05 replaces it with a synthesised
/// market note and a template fallback. No CV text is anywhere near this handler — it reads scores of a fit
/// already judged, never the CV itself (F4 invariant: the CV crosses exactly one boundary).</para>
/// </summary>
public sealed class DigestAssembler(
    IRunRepository runs,
    IDigestScopeQuery scope,
    IDegradedCoverageQuery degraded,
    IDigestRepository digests,
    IIdGenerator ids,
    DigestOptions options,
    IClock clock,
    ILogger<DigestAssembler> logger)
{
    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IDigestScopeQuery _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    private readonly IDegradedCoverageQuery _degraded = degraded ?? throw new ArgumentNullException(nameof(degraded));
    private readonly IDigestRepository _digests = digests ?? throw new ArgumentNullException(nameof(digests));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly DigestOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<DigestAssembler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(RankingCompleted message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var run = await _runs.FindAsync(message.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogWarning("RankingCompleted for unknown Run {RunId}; ignoring.", message.RunId);
            return;
        }

        var existing = await _digests.FindByRunAsync(run.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            // One digest per Run (uq_digests_run). A replayed completion re-emits the same DigestReady for the
            // already-assembled digest rather than building a second one — idempotent on the RunId.
            _logger.LogInformation("Run {RunId} already has a digest; re-publishing DigestReady.", run.Id);
            await PublishReadyAsync(bus, existing).ConfigureAwait(false);
            return;
        }

        var candidates = await _scope.CandidatesAsync(run.Id, cancellationToken).ConfigureAwait(false);
        var degradedSources = await _degraded.DegradedSourcesAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);

        var digest = Assemble(run.Id, run.JobsInScope, run.JobsCarriedOver, candidates, degradedSources);

        // Persist the whole digest before anything is sent (S2): delivery replays stored state.
        _digests.Add(digest);
        await _digests.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await PublishReadyAsync(bus, digest).ConfigureAwait(false);

        _logger.LogInformation(
            "Assembled digest {DigestId} for Run {RunId}: {Cards} cards, {Suppressed} suppressed.",
            digest.Id, run.Id, digest.Cards.Count, digest.SuppressedCount);
    }

    private Digest Assemble(
        Guid runId,
        int totalNewJobs,
        int carriedOverCount,
        IReadOnlyList<DigestCandidate> candidates,
        IReadOnlyList<DegradedSource> degradedSources)
    {
        var digestId = _ids.NewId();

        // Shown candidates at or above the card threshold, best first, each carrying at least one reason — a
        // reason-less card cannot be constructed (invariant 4, AC-02), so it is excluded here rather than
        // thrown on. The query already orders by score descending then job id, so selection is deterministic.
        var carded = candidates
            .Where(c => !c.Suppressed && c.FinalScore >= _options.CardScoreThreshold)
            .Where(c => c.Reasons.Any(r => !string.IsNullOrWhiteSpace(r)))
            .Take(_options.MaxCards)
            .Select((c, index) => new DigestCard(
                _ids.NewId(), digestId, c.JobId, runId, rank: index + 1, c.FinalScore, c.Reasons,
                applyUrlVerified: false))
            .ToList();

        var strongMatches = candidates.Count(c => !c.Suppressed && c.FinalScore >= _options.CardScoreThreshold);

        var suppressed = candidates.Where(c => c.Suppressed).ToList();
        var breakdown = SuppressionSummarizer.Summarize(suppressed);

        var avgSalaryUsd = AverageSalaryUsd(candidates);

        var degradedLabels = degradedSources
            .Select(s => $"{s.CompanyName} ({s.AtsKind})")
            .ToList();

        return new Digest(
            digestId,
            runId,
            totalNewJobs,
            strongMatches,
            avgSalaryUsd,
            suppressed.Count,
            breakdown,
            carriedOverCount,
            degradedLabels,
            narrative: null,
            NarrativeSource.Template,
            promptVersion: null,
            carded,
            _clock.UtcNow);
    }

    /// <summary>
    /// The mean of the shown candidates' USD salaries, or null when too few carry one (better absent than
    /// misleading). Only USD-denominated figures are averaged — there is no FX conversion, because a
    /// fabricated rate would corrupt the number the Owner reads at a glance.
    /// </summary>
    private decimal? AverageSalaryUsd(IReadOnlyList<DigestCandidate> candidates)
    {
        var salaries = candidates
            .Where(c => !c.Suppressed && c.SalaryUsd is > 0m)
            .Select(c => c.SalaryUsd!.Value)
            .ToList();

        if (salaries.Count < _options.MinSalariesForAverage)
        {
            return null;
        }

        return Math.Round(salaries.Average(), 2, MidpointRounding.AwayFromZero);
    }

    private async Task PublishReadyAsync(IMessageBus bus, Digest digest) =>
        await bus.PublishAsync(new DigestReady(digest.RunId, digest.Id, digest.Cards.Count, _clock.UtcNow))
            .ConfigureAwait(false);
}
