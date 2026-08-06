using JobHunter.Application.Reporting;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Sources;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Reporting;

/// <summary>
/// T03: the digest-assembly step (F5 SAD §6.1). Consumes <see cref="RankingCompleted"/>, loads the Run's
/// scored candidates, selects and snapshots the cards, builds the reconciling suppression breakdown, and
/// persists the digest <em>before</em> publishing <see cref="DigestReady"/>. The properties that carry the
/// feature: a card exists only at or above the threshold and never more than the cap; the score and reasons
/// are <em>snapshotted</em> so a re-score cannot change a delivered digest; a reason-less candidate is
/// <em>excluded</em> (invariant 4, AC-02); the suppression breakdown <em>reconciles</em> to the suppressed
/// count (invariant 11, AC-07); the average salary is <em>null</em> below a few salaried jobs; and the digest
/// is <em>persisted before</em> the event is published (S2). Every collaborator is substituted or faked, so
/// these are zero-database unit tests.
///
/// <para>T04 adds apply-link verification: a confirmed-unreachable link drops its card and flags the job for
/// closure (<see cref="ApplyDestinationUnreachable"/>), a timeout or robots refusal keeps the card but marks
/// it unverified, ranks stay contiguous after a drop, and only the selected cards — never every score — are
/// probed. The verifier is substituted, so these stay zero-network.</para>
/// </summary>
public sealed class DigestAssemblerTests
{
    private static readonly DateTimeOffset RunStart = new(2026, 8, 4, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 6, 0, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IDigestScopeQuery _scope = Substitute.For<IDigestScopeQuery>();
    private readonly IDegradedCoverageQuery _degraded = Substitute.For<IDegradedCoverageQuery>();
    private readonly IActiveCompanyCountQuery _activeCompanies = Substitute.For<IActiveCompanyCountQuery>();
    private readonly FakeDigestRepository _digests = new();
    private readonly IApplyLinkVerifier _verifier = Substitute.For<IApplyLinkVerifier>();
    private readonly INarrativeSynthesizer _narrative = Substitute.For<INarrativeSynthesizer>();
    private readonly SequentialIdGenerator _ids = new();
    private readonly FakeClock _clock = new(Now);
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private bool _digestSavedWhenFirstPublished = true;

    public DigestAssemblerTests()
    {
        // By default every apply link is reachable — the T04 tests override this per URL to exercise the
        // confirmed-unreachable and unverified paths; the T03 tests are indifferent to verification.
        _verifier.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApplyLinkStatus.Reachable);

        _degraded.DegradedSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DegradedSource>());

        // By default the synthesiser returns a template note — the T03/T04 tests are indifferent to its
        // provenance; the T05 tests exercise the synthesiser itself directly. A template result keeps the
        // assembler's own tests a pure unit with no batch machinery in scope.
        _narrative.SynthesizeAsync(Arg.Any<Guid>(), Arg.Any<NarrativeInput>(), Arg.Any<CancellationToken>())
            .Returns(call => NarrativeResult.Template(
                NarrativeTemplate.Render((NarrativeInput)call[1]!)));

        // The moment the first message is published, the digest must already be committed (S2). Capture the
        // repository state at that instant so the persist-before-publish property is observed, not assumed.
        // CA2012: NSubstitute's fluent Returns takes the arranged ValueTask as its receiver and never leaves
        // it unconsumed — the analyzer cannot see through the extension, so the suppression is on the arrange.
        var seenPublish = false;
#pragma warning disable CA2012
        _bus.PublishAsync(Arg.Any<DigestReady>())
            .Returns(_ =>
            {
                if (!seenPublish)
                {
                    seenPublish = true;
                    _digestSavedWhenFirstPublished = _digests.Saved.Count > 0;
                }

                return ValueTask.CompletedTask;
            });
#pragma warning restore CA2012
    }

    private DigestAssembler CreateHandler(DigestOptions? options = null) =>
        new(_runs, _scope, _degraded, _activeCompanies, _digests, _verifier, _narrative, _ids,
            options ?? new DigestOptions(), new ApplyVerificationOptions(), _clock,
            NullLogger<DigestAssembler>.Instance);

    private static Run RankingCompletedRun(int jobsInScope = 20, int carriedOver = 0)
    {
        var run = new Run(RunId, RunStart.AddHours(-24), RunStart, 2.00m, RunStart.AddMinutes(-5));
        run.SetScope(jobsInScope);
        run.RecordCarryOver(carriedOver);
        run.TransitionTo(RunState.Enriching, RunStart);
        run.TransitionTo(RunState.Matching, RunStart);
        run.TransitionTo(RunState.Ranking, RunStart);
        run.TransitionTo(RunState.Researching, RunStart);
        return run;
    }

    private void GivenRun(Run run) =>
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);

    private void GivenCandidates(params DigestCandidate[] candidates) =>
        _scope.CandidatesAsync(RunId, Arg.Any<CancellationToken>()).Returns(candidates);

    private static DigestCandidate Shown(Guid id, decimal score, decimal? salaryUsd = null, params string[] reasons) =>
        new(id, score, Suppressed: false, SuppressionReason: null,
            reasons.Length == 0 ? ["Strong fit"] : reasons, salaryUsd, ApplyUrlFor(id));

    private static DigestCandidate Suppressed(Guid id, string reason, decimal score = 20m) =>
        new(id, score, Suppressed: true, reason, ["Below the bar"], SalaryUsd: null, ApplyUrlFor(id));

    // A deterministic, unique apply URL per candidate: the T04 tests key the verifier's verdict on it.
    private static string ApplyUrlFor(Guid id) => $"https://apply.example.com/{id:N}";

    private static RankingCompleted Message() =>
        new(RunId, RankedCount: 3, SuppressedCount: 0, TopJobIds: [], Now);

    private List<object> Publishes() =>
        _bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IMessageBus.PublishAsync))
            .Select(c => c.GetArguments())
            .Where(a => a.Length > 0 && a[0] is not null)
            .Select(a => a[0]!)
            .ToList();

    // ---- card selection: threshold and cap ----------------------------------------------------

    [Fact]
    public async Task A_score_at_the_threshold_is_carded_and_one_below_it_is_not()
    {
        GivenRun(RankingCompletedRun());
        var at = Guid.CreateVersion7();
        var below = Guid.CreateVersion7();
        GivenCandidates(Shown(at, 70m), Shown(below, 69m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        digest.Cards.Select(c => c.JobId).ShouldBe([at]);
    }

    [Fact]
    public async Task Card_selection_is_capped_at_the_configured_maximum()
    {
        GivenRun(RankingCompletedRun());
        // Eleven candidates all qualify; only the top ten become cards, best score first.
        var ids = Enumerable.Range(0, 11).Select(_ => Guid.CreateVersion7()).ToArray();
        GivenCandidates(ids.Select((id, i) => Shown(id, 100m - i)).ToArray());

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        digest.Cards.Count.ShouldBe(10);
        // The eleventh (lowest) is the one dropped; the cards keep the query's descending order as ranks 1..10.
        digest.Cards.Select(c => c.JobId).ShouldBe(ids.Take(10));
        digest.Cards.Select(c => c.Rank).ShouldBe(Enumerable.Range(1, 10));
    }

    [Fact]
    public async Task The_threshold_and_cap_are_configurable()
    {
        GivenRun(RankingCompletedRun());
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7()).ToArray();
        GivenCandidates(ids.Select((id, i) => Shown(id, 90m - (i * 5))).ToArray());

        await CreateHandler(new DigestOptions { CardScoreThreshold = 80m, MaxCards = 2 })
            .Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        // Scores 90, 85, 80, 75: three clear the 80 bar, but the cap keeps only the top two.
        digest.Cards.Count.ShouldBe(2);
        digest.Cards.Select(c => c.Score).ShouldBe([90m, 85m]);
    }

    // ---- snapshotting: the card copies score and reasons at assembly --------------------------

    [Fact]
    public async Task A_card_snapshots_its_score_and_reasons_from_the_candidate()
    {
        GivenRun(RankingCompletedRun());
        var id = Guid.CreateVersion7();
        GivenCandidates(Shown(id, 88m, null, "Tier-1 AI platform", "Remote EMEA"));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var card = _digests.Saved.ShouldHaveSingleItem().Cards.ShouldHaveSingleItem();
        card.Score.ShouldBe(88m);
        card.Reasons.ShouldBe(["Tier-1 AI platform", "Remote EMEA"]);
        // The key is a pure function of (run, job), so a resumed delivery recomputes it.
        card.Key.ShouldBe(CardKey.For(RunId, id));
    }

    // ---- invariant 4 / AC-02: a reason-less candidate is excluded, not carded -----------------

    [Fact]
    public async Task A_qualifying_candidate_with_no_reasons_is_excluded_from_the_cards()
    {
        GivenRun(RankingCompletedRun());
        var explained = Guid.CreateVersion7();
        var unexplained = Guid.CreateVersion7();
        GivenCandidates(
            Shown(explained, 90m, null, "Strong fit"),
            new DigestCandidate(unexplained, 95m, Suppressed: false, null, [], SalaryUsd: null,
                ApplyUrlFor(unexplained)));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        // The higher-scoring but unexplained job never reaches the Owner (invariant 4).
        digest.Cards.Select(c => c.JobId).ShouldBe([explained]);
    }

    [Fact]
    public async Task A_candidate_whose_only_reasons_are_blank_is_excluded()
    {
        GivenRun(RankingCompletedRun());
        var blank = Guid.CreateVersion7();
        GivenCandidates(new DigestCandidate(blank, 95m, Suppressed: false, null, ["   ", ""], SalaryUsd: null,
            ApplyUrlFor(blank)));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _digests.Saved.ShouldHaveSingleItem().Cards.ShouldBeEmpty();
    }

    // ---- invariant 11 / AC-07: the suppression breakdown reconciles ---------------------------

    [Fact]
    public async Task The_suppression_breakdown_groups_by_reason_and_reconciles_to_the_count()
    {
        GivenRun(RankingCompletedRun());
        GivenCandidates(
            Shown(Guid.CreateVersion7(), 90m),
            Suppressed(Guid.CreateVersion7(), "Below presentation threshold"),
            Suppressed(Guid.CreateVersion7(), "Below presentation threshold"),
            Suppressed(Guid.CreateVersion7(), "Not a target role family: MlResearch"));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        digest.SuppressedCount.ShouldBe(3);
        digest.SuppressionBreakdown.Sum(t => t.Count).ShouldBe(3);
        // Largest bucket first.
        digest.SuppressionBreakdown[0].Reason.ShouldBe("Below presentation threshold");
        digest.SuppressionBreakdown[0].Count.ShouldBe(2);
        digest.SuppressionBreakdown[1].Reason.ShouldBe("Not a target role family: MlResearch");
        digest.SuppressionBreakdown[1].Count.ShouldBe(1);
    }

    // ---- header counts ------------------------------------------------------------------------

    [Fact]
    public async Task Strong_matches_counts_every_shown_score_at_or_above_the_threshold_not_just_the_carded_ten()
    {
        GivenRun(RankingCompletedRun());
        // Twelve shown scores at 70+; the cap shows ten cards but the header counts all twelve strong matches.
        var strong = Enumerable.Range(0, 12).Select(_ => Guid.CreateVersion7())
            .Select((id, i) => Shown(id, 95m - i)).ToArray();
        GivenCandidates(strong);

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        digest.Cards.Count.ShouldBe(10);
        digest.StrongMatches.ShouldBe(12);
    }

    [Fact]
    public async Task The_run_scope_and_carry_over_flow_onto_the_digest()
    {
        GivenRun(RankingCompletedRun(jobsInScope: 42, carriedOver: 4));
        GivenCandidates(Shown(Guid.CreateVersion7(), 90m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        digest.TotalNewJobs.ShouldBe(42);
        digest.CarriedOverCount.ShouldBe(4);
    }

    // ---- average salary: null below the minimum, USD-only -------------------------------------

    [Fact]
    public async Task The_average_salary_is_null_when_fewer_than_three_jobs_carry_one()
    {
        GivenRun(RankingCompletedRun());
        GivenCandidates(
            Shown(Guid.CreateVersion7(), 90m, 100000m),
            Shown(Guid.CreateVersion7(), 85m, 120000m),
            Shown(Guid.CreateVersion7(), 80m, null));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _digests.Saved.ShouldHaveSingleItem().AvgSalaryUsd.ShouldBeNull();
    }

    [Fact]
    public async Task The_average_salary_is_the_mean_of_the_usd_figures_once_there_are_enough()
    {
        GivenRun(RankingCompletedRun());
        GivenCandidates(
            Shown(Guid.CreateVersion7(), 90m, 100000m),
            Shown(Guid.CreateVersion7(), 85m, 120000m),
            Shown(Guid.CreateVersion7(), 80m, 140000m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _digests.Saved.ShouldHaveSingleItem().AvgSalaryUsd.ShouldBe(120000m);
    }

    // ---- degraded sources ---------------------------------------------------------------------

    [Fact]
    public async Task Degraded_sources_are_rendered_onto_the_digest_footer()
    {
        GivenRun(RankingCompletedRun());
        GivenCandidates(Shown(Guid.CreateVersion7(), 90m));
        _degraded.DegradedSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new DegradedSource(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), "Acme", "Greenhouse", 5, Now.AddHours(6)),
            });

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _digests.Saved.ShouldHaveSingleItem().DegradedSources.ShouldBe(["Acme (Greenhouse)"]);
    }

    // ---- S2: persist before publish, and the event carries the digest -------------------------

    [Fact]
    public async Task The_digest_is_persisted_before_digest_ready_is_published()
    {
        GivenRun(RankingCompletedRun());
        GivenCandidates(Shown(Guid.CreateVersion7(), 90m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        // The bus callback captured the repository state at the first publish: the digest was already committed.
        _digestSavedWhenFirstPublished.ShouldBeTrue();
        var ready = Publishes().OfType<DigestReady>().ShouldHaveSingleItem();
        ready.RunId.ShouldBe(RunId);
        ready.DigestId.ShouldBe(_digests.Saved.Single().Id);
        ready.CardCount.ShouldBe(1);
    }

    [Fact]
    public async Task An_empty_run_still_assembles_and_ships_a_digest()
    {
        GivenRun(RankingCompletedRun(jobsInScope: 0));
        GivenCandidates();

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        digest.Cards.ShouldBeEmpty();
        digest.SuppressedCount.ShouldBe(0);
        digest.SuppressionBreakdown.ShouldBeEmpty();
        Publishes().OfType<DigestReady>().ShouldHaveSingleItem().CardCount.ShouldBe(0);
    }

    // ---- T09: the four header modes a day's state earns (ADR-F5-0001) -------------------------

    private static Run RunInState(RunState state, int jobsInScope = 20)
    {
        var run = new Run(RunId, RunStart.AddHours(-24), RunStart, 2.00m, RunStart.AddMinutes(-5));
        run.SetScope(jobsInScope);
        run.TransitionTo(RunState.Enriching, RunStart);
        if (state == RunState.Enriching)
        {
            return run;
        }

        if (state == RunState.CostAborted)
        {
            return run.Abort("Daily budget reached before ranking.", RunStart, costBreach: true);
        }

        run.TransitionTo(RunState.Matching, RunStart);
        run.TransitionTo(RunState.Ranking, RunStart);
        run.TransitionTo(RunState.Researching, RunStart);
        return run;
    }

    [Fact]
    public async Task A_finished_run_with_cards_is_a_full_digest()
    {
        // Reporting/Delivered/Researching with at least one card → the normal header (ADR-F5-0001 row 1).
        GivenRun(RunInState(RunState.Researching));
        GivenCandidates(Shown(Guid.CreateVersion7(), 90m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _digests.Saved.ShouldHaveSingleItem().Mode.ShouldBe(DigestMode.Full);
    }

    [Fact]
    public async Task A_finished_run_with_nothing_shown_or_suppressed_is_nothing_new_and_states_the_scope()
    {
        // A finished run that surfaced no cards and suppressed nothing → the "nothing new, nothing wrong"
        // header, which states how many companies were scanned (AC-05, ADR-F5-0001 row 2).
        GivenRun(RunInState(RunState.Researching));
        GivenCandidates();
        _activeCompanies.ActiveCompanyCountAsync(Arg.Any<CancellationToken>()).Returns(137);

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        digest.Mode.ShouldBe(DigestMode.NothingNew);
        digest.CompaniesChecked.ShouldBe(137);
        // Only a NothingNew day pays for the company count; the query is not run on the other paths.
        await _activeCompanies.Received(1).ActiveCompanyCountAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_still_running_run_is_a_partial_digest_naming_what_is_carried_over()
    {
        // The 06:45 deadline caught the run mid-flight (still Enriching) → the "analysis incomplete" header,
        // which names what is missing via the carried-over count (AC-06, ADR-F5-0001 row 3).
        var run = RunInState(RunState.Enriching);
        run.RecordCarryOver(9);
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns(run);
        GivenCandidates(Shown(Guid.CreateVersion7(), 90m));

        await CreateHandler().Handle(new DigestAssemblyDue(Now), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        digest.Mode.ShouldBe(DigestMode.Partial);
        digest.CarriedOverCount.ShouldBe(9);
        // The company count is a NothingNew concern only — a partial day never runs it.
        await _activeCompanies.DidNotReceive().ActiveCompanyCountAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cost_aborted_run_is_a_budget_reached_digest_stating_how_far_it_got()
    {
        // The run hit the ceiling and aborted → the "budget reached" header, reduced but delivered, stating
        // how many candidates were analysed before the abort (AC-06, ADR-F5-0001 row 4).
        var run = RunInState(RunState.CostAborted);
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns(run);
        var shown = Guid.CreateVersion7();
        GivenCandidates(Shown(shown, 90m), Suppressed(Guid.CreateVersion7(), "Below the bar"));

        await CreateHandler().Handle(new DigestAssemblyDue(Now), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        digest.Mode.ShouldBe(DigestMode.BudgetReached);
        // Analysed-count is every candidate the run managed to score before the ceiling, shown or suppressed.
        digest.AnalysedCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_assembly_deadline_with_no_run_at_all_is_silence_not_a_digest()
    {
        // No Run row means the 02:00 tick never opened the day — the R1 silence case, not a degraded digest.
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);

        await CreateHandler().Handle(new DigestAssemblyDue(Now), _bus, CancellationToken.None);

        _digests.Saved.ShouldBeEmpty();
        Publishes().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_assembly_deadline_reuses_an_already_assembled_digest()
    {
        // The happy path already assembled earlier on RankingCompleted; the 06:45 backstop re-emits rather
        // than building a second one (idempotent on uq_digests_run).
        GivenRun(RunInState(RunState.Researching));
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns(RunInState(RunState.Researching));
        GivenCandidates(Shown(Guid.CreateVersion7(), 90m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);
        await CreateHandler().Handle(new DigestAssemblyDue(Now), _bus, CancellationToken.None);

        _digests.Saved.Count.ShouldBe(1);
        _digests.AddCount.ShouldBe(1);
        Publishes().OfType<DigestReady>().Count().ShouldBe(2);
    }

    // ---- T04: apply-link verification ---------------------------------------------------------

    private void GivenApplyLink(Guid jobId, ApplyLinkStatus status) =>
        _verifier.VerifyAsync(ApplyUrlFor(jobId), Arg.Any<CancellationToken>()).Returns(status);

    [Fact]
    public async Task A_confirmed_unreachable_apply_destination_drops_the_card()
    {
        GivenRun(RankingCompletedRun());
        var reachable = Guid.CreateVersion7();
        var dead = Guid.CreateVersion7();
        GivenCandidates(Shown(reachable, 90m), Shown(dead, 85m));
        GivenApplyLink(dead, ApplyLinkStatus.ConfirmedUnreachable);

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        // The dead link is not presented as an actionable card (AC-11); the reachable one is.
        var digest = _digests.Saved.ShouldHaveSingleItem();
        digest.Cards.Select(c => c.JobId).ShouldBe([reachable]);
    }

    [Fact]
    public async Task A_confirmed_unreachable_link_flags_its_job_for_the_lifecycle_sweep()
    {
        GivenRun(RankingCompletedRun());
        var dead = Guid.CreateVersion7();
        GivenCandidates(Shown(dead, 90m));
        GivenApplyLink(dead, ApplyLinkStatus.ConfirmedUnreachable);

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        // The read path never closes the job itself — it publishes so F2's lifecycle handler can (AC-11).
        var flagged = Publishes().OfType<ApplyDestinationUnreachable>().ShouldHaveSingleItem();
        flagged.JobId.ShouldBe(dead);
        flagged.ConfirmedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task A_reachable_card_is_marked_verified_and_raises_no_flag()
    {
        GivenRun(RankingCompletedRun());
        var reachable = Guid.CreateVersion7();
        GivenCandidates(Shown(reachable, 90m));
        GivenApplyLink(reachable, ApplyLinkStatus.Reachable);

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var card = _digests.Saved.ShouldHaveSingleItem().Cards.ShouldHaveSingleItem();
        card.ApplyUrlVerified.ShouldBeTrue();
        Publishes().OfType<ApplyDestinationUnreachable>().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unverified_link_keeps_the_card_but_marks_it_unverified()
    {
        GivenRun(RankingCompletedRun());
        var slow = Guid.CreateVersion7();
        GivenCandidates(Shown(slow, 90m));
        GivenApplyLink(slow, ApplyLinkStatus.Unverified);

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        // A timeout or a robots refusal is not a closed job: the card stays, flagged "link unverified" (D3).
        var card = _digests.Saved.ShouldHaveSingleItem().Cards.ShouldHaveSingleItem();
        card.JobId.ShouldBe(slow);
        card.ApplyUrlVerified.ShouldBeFalse();
        Publishes().OfType<ApplyDestinationUnreachable>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Ranks_are_contiguous_after_a_dropped_card()
    {
        GivenRun(RankingCompletedRun());
        var top = Guid.CreateVersion7();
        var dead = Guid.CreateVersion7();
        var third = Guid.CreateVersion7();
        GivenCandidates(Shown(top, 95m), Shown(dead, 90m), Shown(third, 85m));
        GivenApplyLink(dead, ApplyLinkStatus.ConfirmedUnreachable);

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        // Dropping the middle card must not leave a rank gap: the survivors are 1 then 2, in score order.
        var cards = _digests.Saved.ShouldHaveSingleItem().Cards;
        cards.Select(c => (c.JobId, c.Rank)).ShouldBe([(top, 1), (third, 2)]);
    }

    [Fact]
    public async Task Only_selected_cards_are_verified_not_every_score()
    {
        GivenRun(RankingCompletedRun());
        var carded = Guid.CreateVersion7();
        var belowBar = Guid.CreateVersion7();
        var suppressed = Guid.CreateVersion7();
        GivenCandidates(Shown(carded, 90m), Shown(belowBar, 50m), Suppressed(suppressed, "Below the bar"));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        // Verification is a network probe: it runs only on the cards worth showing, never on the whole Run.
        await _verifier.Received(1).VerifyAsync(ApplyUrlFor(carded), Arg.Any<CancellationToken>());
        await _verifier.DidNotReceive().VerifyAsync(ApplyUrlFor(belowBar), Arg.Any<CancellationToken>());
        await _verifier.DidNotReceive().VerifyAsync(ApplyUrlFor(suppressed), Arg.Any<CancellationToken>());
    }

    // ---- T13: near-duplicate grouping at assembly ---------------------------------------------

    // A shown candidate that carries the (company, normalised title) grouping key T13 collapses on. The
    // default Shown() leaves both empty, so every existing test's candidates stand alone — grouping is opt-in.
    private static DigestCandidate ShownFor(
        Guid id, Guid companyId, string normalisedTitle, decimal score, params string[] reasons) =>
        new(id, score, Suppressed: false, SuppressionReason: null,
            reasons.Length == 0 ? ["Strong fit"] : reasons, SalaryUsd: null, ApplyUrlFor(id),
            companyId, normalisedTitle);

    [Fact]
    public async Task Two_postings_of_the_same_opening_collapse_to_one_card()
    {
        GivenRun(RankingCompletedRun());
        var company = Guid.CreateVersion7();
        var top = Guid.CreateVersion7();
        var dup = Guid.CreateVersion7();
        // Same company, same normalised title, posted twice — one real opening under two board listings.
        GivenCandidates(
            ShownFor(top, company, "staff sre", 90m),
            ShownFor(dup, company, "staff sre", 85m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        // One card shown — the higher-scored posting is the representative; the duplicate is grouped away, not
        // a second card, so the Owner never sees the same role twice.
        var card = digest.Cards.ShouldHaveSingleItem();
        card.JobId.ShouldBe(top);
        card.GroupedJobIds.ShouldBe([dup]);
    }

    [Fact]
    public async Task Distinct_roles_at_the_same_company_are_not_merged()
    {
        GivenRun(RankingCompletedRun());
        var company = Guid.CreateVersion7();
        var sre = Guid.CreateVersion7();
        var backend = Guid.CreateVersion7();
        GivenCandidates(
            ShownFor(sre, company, "staff sre", 90m),
            ShownFor(backend, company, "senior backend engineer", 85m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        // Different titles are different openings: two cards, neither grouping the other away.
        digest.Cards.Select(c => c.JobId).ShouldBe([sre, backend]);
        digest.Cards.ShouldAllBe(c => c.GroupedJobIds.Count == 0);
    }

    [Fact]
    public async Task The_same_title_at_different_companies_stays_two_cards()
    {
        GivenRun(RankingCompletedRun());
        var acme = Guid.CreateVersion7();
        var globex = Guid.CreateVersion7();
        var atAcme = Guid.CreateVersion7();
        var atGlobex = Guid.CreateVersion7();
        GivenCandidates(
            ShownFor(atAcme, acme, "staff sre", 90m),
            ShownFor(atGlobex, globex, "staff sre", 85m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        // A shared title is not a shared opening across companies: both cards stand.
        digest.Cards.Select(c => c.JobId).ShouldBe([atAcme, atGlobex]);
    }

    [Fact]
    public async Task Grouping_shrinks_the_shown_count_but_keeps_ranks_contiguous()
    {
        GivenRun(RankingCompletedRun());
        var company = Guid.CreateVersion7();
        var top = Guid.CreateVersion7();
        var dup = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        GivenCandidates(
            ShownFor(top, company, "staff sre", 95m),
            ShownFor(dup, company, "staff sre", 90m),
            ShownFor(other, Guid.CreateVersion7(), "principal engineer", 85m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        // Three qualifying candidates, but the two duplicates collapse: the "N shown" count is the two
        // representatives, ranked 1 then 2 with no gap (AC-04's shown count reflects grouping).
        digest.Cards.Select(c => (c.JobId, c.Rank)).ShouldBe([(top, 1), (other, 2)]);
    }

    [Fact]
    public async Task A_replay_reproduces_the_same_grouping()
    {
        GivenRun(RankingCompletedRun());
        var company = Guid.CreateVersion7();
        var top = Guid.CreateVersion7();
        var dup = Guid.CreateVersion7();
        GivenCandidates(
            ShownFor(top, company, "staff sre", 90m),
            ShownFor(dup, company, "staff sre", 85m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);
        var first = _digests.Saved.ShouldHaveSingleItem();

        // A replayed completion finds the committed digest and re-emits it: the grouping it snapshotted is the
        // grouping a resumed delivery replays, not a fresh (and possibly different) computation.
        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _digests.Saved.Count.ShouldBe(1);
        var card = first.Cards.ShouldHaveSingleItem();
        card.JobId.ShouldBe(top);
        card.GroupedJobIds.ShouldBe([dup]);
    }

    // ---- idempotence: one digest per Run ------------------------------------------------------

    [Fact]
    public async Task A_second_completion_for_the_same_run_reuses_the_digest_and_writes_nothing_new()
    {
        GivenRun(RankingCompletedRun());
        GivenCandidates(Shown(Guid.CreateVersion7(), 90m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);
        var firstId = _digests.Saved.Single().Id;

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _digests.Saved.Count.ShouldBe(1);
        _digests.AddCount.ShouldBe(1);
        // Both passes publish DigestReady for the same digest, so a lost first event is recoverable.
        Publishes().OfType<DigestReady>().Count().ShouldBe(2);
        Publishes().OfType<DigestReady>().ShouldAllBe(r => r.DigestId == firstId);
    }

    // ---- guard --------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_run_is_ignored()
    {
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns((Run?)null);

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _digests.Saved.ShouldBeEmpty();
        Publishes().ShouldBeEmpty();
    }

    /// <summary>
    /// Models the one-digest-per-Run write path: <see cref="Add"/> stages, <see cref="SaveChangesAsync"/>
    /// commits, and <see cref="FindByRunAsync"/> returns a committed digest so a replay is a no-op. The S2
    /// property (persist before publish) is observed at the bus callback, not recorded here.
    /// </summary>
    private sealed class FakeDigestRepository : IDigestRepository
    {
        private Digest? _staged;

        public List<Digest> Saved { get; } = [];

        public int AddCount { get; private set; }

        public void Add(Digest digest)
        {
            AddCount++;
            _staged = digest;
        }

        public Task<Digest?> FindByRunAsync(Guid runId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(d => d.RunId == runId));

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_staged is not null)
            {
                Saved.Add(_staged);
                _staged = null;
                return Task.FromResult(1);
            }

            return Task.FromResult(0);
        }
    }
}
