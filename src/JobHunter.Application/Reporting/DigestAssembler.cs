using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Sources;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Reporting;

/// <summary>
/// The digest-assembly step (F5 SAD §6.1). Consumes <see cref="RankingCompleted"/>: it loads the Run's scored
/// candidates (each joined to its current-match reasons and USD salary), selects the cards (score at or above
/// the threshold, capped at the configured maximum, each snapshotting its score and reasons), verifies their
/// apply destinations (SAD §11 D3, AC-11), builds the suppression breakdown, gathers the carried-over and
/// degraded-source counts, persists the whole digest <em>before</em> anything is sent (S2), and publishes
/// <see cref="DigestReady"/>.
///
/// <para>Apply-link verification decides each selected card's fate by value, never by an exception: a
/// <see cref="ApplyLinkStatus.ConfirmedUnreachable"/> link — a definitive 4xx/5xx or a DNS failure — drops
/// the card and publishes <see cref="ApplyDestinationUnreachable"/> so F2 (which owns closure) can close the
/// job; a <see cref="ApplyLinkStatus.Unverified"/> result — a timeout or a robots refusal — keeps the card
/// with <c>apply_url_verified = false</c>, because a slow or unfetchable link is not a closed job (D3).
/// Verification runs with bounded parallelism and a short per-probe timeout, so it never exceeds the assembly
/// window, and every probe goes through the shared politeness gate (QG-2).</para>
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
/// writes nothing new. The market note comes from the bounded, best-effort <see cref="INarrativeSynthesizer"/>
/// (F5-T05): a synthesised note when one lands within budget, a deterministic template otherwise — either
/// way the digest ships. No CV text is anywhere near this handler — it reads scores of a fit already judged,
/// and the synthesiser sees only aggregate counts, never the CV itself (F4 invariant: the CV crosses exactly
/// one boundary).</para>
/// </summary>
public sealed class DigestAssembler(
    IRunRepository runs,
    IDigestScopeQuery scope,
    IDegradedCoverageQuery degraded,
    IActiveCompanyCountQuery activeCompanies,
    IDigestRepository digests,
    IApplyLinkVerifier applyLinkVerifier,
    INarrativeSynthesizer narrativeSynthesizer,
    IIdGenerator ids,
    DigestOptions options,
    ApplyVerificationOptions applyVerification,
    IClock clock,
    ILogger<DigestAssembler> logger)
{
    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IDigestScopeQuery _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    private readonly IDegradedCoverageQuery _degraded = degraded ?? throw new ArgumentNullException(nameof(degraded));
    private readonly IActiveCompanyCountQuery _activeCompanies = activeCompanies
        ?? throw new ArgumentNullException(nameof(activeCompanies));
    private readonly IDigestRepository _digests = digests ?? throw new ArgumentNullException(nameof(digests));
    private readonly IApplyLinkVerifier _applyLinkVerifier = applyLinkVerifier
        ?? throw new ArgumentNullException(nameof(applyLinkVerifier));
    private readonly INarrativeSynthesizer _narrativeSynthesizer = narrativeSynthesizer
        ?? throw new ArgumentNullException(nameof(narrativeSynthesizer));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly DigestOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ApplyVerificationOptions _applyVerification = applyVerification
        ?? throw new ArgumentNullException(nameof(applyVerification));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<DigestAssembler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// The early, happy-path trigger: ranking completed, so assemble the digest now rather than waiting for
    /// the 06:45 tick (SAD §6.1). Idempotent on <c>uq_digests_run</c> — a replayed completion re-emits the
    /// existing digest rather than building a second one.
    /// </summary>
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

        await AssembleForRunAsync(run, bus, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The 06:45 Europe/Kyiv deadline (ADR-F5-0001, SAD §6.3): assemble the day's digest against whatever
    /// state the Run is in. It resolves the most recent Run — including a terminal <c>CostAborted</c>/
    /// <c>Failed</c> one, which the happy path never assembled — and assembles the variant that state earns.
    /// When there is no Run at all the 02:00 tick never fired, which is the silence case the runbook alerts on
    /// (R1), not a digest this handler can invent. Idempotent: if the happy path already assembled earlier,
    /// this re-emits <see cref="DigestReady"/> and writes nothing new.
    /// </summary>
    public async Task Handle(DigestAssemblyDue message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var run = await _runs.FindMostRecentRunAsync(cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            // No Run row means the 02:00 tick never opened the day — genuine silence, which the R1 runbook
            // alerts on. There is nothing to assemble a digest from, so this is not a degraded digest path.
            _logger.LogWarning("DigestAssemblyDue at {DueAt} but no Run exists for the day; nothing to assemble.", message.DueAt);
            return;
        }

        await AssembleForRunAsync(run, bus, cancellationToken).ConfigureAwait(false);
    }

    private async Task AssembleForRunAsync(Run run, IMessageBus bus, CancellationToken cancellationToken)
    {
        var existing = await _digests.FindByRunAsync(run.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            // One digest per Run (uq_digests_run). A replayed trigger re-emits the same DigestReady for the
            // already-assembled digest rather than building a second one — idempotent on the RunId.
            _logger.LogInformation("Run {RunId} already has a digest; re-publishing DigestReady.", run.Id);
            await PublishReadyAsync(bus, existing).ConfigureAwait(false);
            return;
        }

        var candidates = await _scope.CandidatesAsync(run.Id, cancellationToken).ConfigureAwait(false);
        var degradedSources = await _degraded.DegradedSourcesAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);

        var digestId = _ids.NewId();

        // Select the cards, group away near-duplicates, then verify the survivors' apply destinations. Grouping
        // runs on the selected set, after selection and before verification and persistence, so the same real
        // opening posted twice becomes one card and the grouping is snapshotted onto the digest — a replay
        // reproduces it (F5-T13, ADR-F2-0001). A confirmed-unreachable link drops its card and flags the job for
        // closure; a timeout or robots refusal keeps the card, unverified (AC-11).
        var selected = SelectCandidates(candidates);
        var groups = NearDuplicateGrouper.Group(selected);
        var representatives = groups.Select(g => g.Representative).ToList();
        var verified = await VerifyApplyLinksAsync(representatives, cancellationToken).ConfigureAwait(false);

        var groupedByJob = groups.ToDictionary(g => g.Representative.JobId, g => g.GroupedJobIds);

        var cards = verified
            .Where(v => v.Status != ApplyLinkStatus.ConfirmedUnreachable)
            .Select((v, index) => new DigestCard(
                _ids.NewId(), digestId, v.Candidate.JobId, run.Id, rank: index + 1, v.Candidate.FinalScore,
                v.Candidate.Reasons, applyUrlVerified: v.Status == ApplyLinkStatus.Reachable,
                groupedJobIds: groupedByJob[v.Candidate.JobId]))
            .ToList();

        var unreachableJobIds = verified
            .Where(v => v.Status == ApplyLinkStatus.ConfirmedUnreachable)
            .Select(v => v.Candidate.JobId)
            .ToList();

        // The header shape the Run earned at this point, frozen onto the digest so delivery replays it rather
        // than re-classifying a run that has since moved on (ADR-F5-0001, S2). Suppressed count drives the
        // Full-vs-NothingNew split, so it is computed before the mode.
        var suppressedCount = candidates.Count(c => c.Suppressed);
        var mode = DigestModeResolver.Resolve(run.State, cards.Count, suppressedCount);

        // The two counts only some headers show. Companies-checked is the scope a NothingNew day looked across
        // (AC-05); analysed-count is how far a budget-aborted run got before the ceiling (AC-06). Read only when
        // the mode uses it, so a normal day pays for neither.
        var companiesChecked = mode == DigestMode.NothingNew
            ? await _activeCompanies.ActiveCompanyCountAsync(cancellationToken).ConfigureAwait(false)
            : 0;
        var analysedCount = mode == DigestMode.BudgetReached ? candidates.Count : 0;

        var digest = await Assemble(
            digestId, run.Id, mode, run.JobsInScope, run.JobsCarriedOver, companiesChecked, analysedCount,
            candidates, cards, degradedSources, cancellationToken)
            .ConfigureAwait(false);

        // Persist the whole digest before anything is sent (S2): delivery replays stored state.
        _digests.Add(digest);
        await _digests.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Flag every job whose apply link was confirmed dead so the layer that owns closure (F2) can close it.
        // This is a flag, not a close: the read path never mutates a Job — it publishes and lets the lifecycle
        // handler transition the aggregate (AC-11). Keyed on (JobId, ConfirmedAt) so a replay collapses.
        foreach (var jobId in unreachableJobIds)
        {
            await bus.PublishAsync(new ApplyDestinationUnreachable(jobId, _clock.UtcNow, _clock.UtcNow))
                .ConfigureAwait(false);
        }

        await PublishReadyAsync(bus, digest).ConfigureAwait(false);

        _logger.LogInformation(
            "Assembled digest {DigestId} for Run {RunId}: {Cards} cards, {Suppressed} suppressed, {Unreachable} dropped as unreachable.",
            digest.Id, run.Id, digest.Cards.Count, digest.SuppressedCount, unreachableJobIds.Count);
    }

    /// <summary>
    /// The shown candidates at or above the card threshold, best first, each carrying at least one reason and
    /// capped at the maximum. A reason-less candidate is excluded rather than thrown on, so an unexplained job
    /// never reaches the Owner (invariant 4, AC-02). The query already orders by score descending then job id,
    /// so selection is deterministic (QG-3). Selection happens before verification: the cap is on what is
    /// worth probing, and a confirmed-unreachable link then drops out of the selected set.
    /// </summary>
    private List<DigestCandidate> SelectCandidates(IReadOnlyList<DigestCandidate> candidates) =>
        candidates
            .Where(c => !c.Suppressed && c.FinalScore >= _options.CardScoreThreshold)
            .Where(c => c.Reasons.Any(r => !string.IsNullOrWhiteSpace(r)))
            .Take(_options.MaxCards)
            .ToList();

    /// <summary>
    /// Verifies the selected candidates' apply links with bounded parallelism (SAD §11 D3), preserving their
    /// score order so the surviving cards' ranks stay deterministic. Each probe is capped by the verifier's
    /// own short timeout, so verification never exceeds the assembly window; a slow link comes back Unverified,
    /// not dropped. The verifier only ever returns a value — a dead host is a status, not a thrown fault — so
    /// one bad link cannot take the digest down.
    /// </summary>
    private async Task<IReadOnlyList<VerifiedCandidate>> VerifyApplyLinksAsync(
        List<DigestCandidate> selected,
        CancellationToken cancellationToken)
    {
        if (selected.Count == 0)
        {
            return [];
        }

        var statuses = new ApplyLinkStatus[selected.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, selected.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _applyVerification.MaxParallelism,
                CancellationToken = cancellationToken,
            },
            async (index, ct) =>
            {
                statuses[index] = await _applyLinkVerifier
                    .VerifyAsync(selected[index].ApplyUrl, ct)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);

        return selected
            .Select((candidate, index) => new VerifiedCandidate(candidate, statuses[index]))
            .ToList();
    }

    private async Task<Digest> Assemble(
        Guid digestId,
        Guid runId,
        DigestMode mode,
        int totalNewJobs,
        int carriedOverCount,
        int companiesChecked,
        int analysedCount,
        IReadOnlyList<DigestCandidate> candidates,
        List<DigestCard> cards,
        IReadOnlyList<DegradedSource> degradedSources,
        CancellationToken cancellationToken)
    {
        var strongMatches = candidates.Count(c => !c.Suppressed && c.FinalScore >= _options.CardScoreThreshold);

        var suppressed = candidates.Where(c => c.Suppressed).ToList();
        var breakdown = SuppressionSummarizer.Summarize(suppressed);

        var avgSalaryUsd = AverageSalaryUsd(candidates);

        var degradedLabels = degradedSources
            .Select(s => $"{s.CompanyName} ({s.AtsKind})")
            .ToList();

        // The market note is synthesised from the same aggregate numbers the header and footer already carry —
        // never the CV, never a card reason (F4 invariant). The synthesiser is bounded and best-effort: it
        // returns a model note if one lands in time, otherwise a deterministic template, so a provider outage
        // or an exhausted budget never delays the digest (F5 T05, ADR-F5-0001).
        var narrativeInput = new NarrativeInput(
            totalNewJobs, strongMatches, cards.Count, avgSalaryUsd, suppressed.Count, carriedOverCount, degradedLabels.Count);
        var narrative = await _narrativeSynthesizer
            .SynthesizeAsync(runId, narrativeInput, cancellationToken)
            .ConfigureAwait(false);

        return new Digest(
            digestId,
            runId,
            mode,
            totalNewJobs,
            strongMatches,
            avgSalaryUsd,
            suppressed.Count,
            breakdown,
            carriedOverCount,
            companiesChecked,
            analysedCount,
            degradedLabels,
            narrative.Narrative,
            narrative.Source,
            narrative.PromptVersion,
            cards,
            _clock.UtcNow);
    }

    /// <summary>One selected candidate paired with the verdict of its apply-link probe.</summary>
    private sealed record VerifiedCandidate(DigestCandidate Candidate, ApplyLinkStatus Status);

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
