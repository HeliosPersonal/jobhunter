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
/// </summary>
public sealed class DigestAssemblerTests
{
    private static readonly DateTimeOffset RunStart = new(2026, 8, 4, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 6, 0, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IDigestScopeQuery _scope = Substitute.For<IDigestScopeQuery>();
    private readonly IDegradedCoverageQuery _degraded = Substitute.For<IDegradedCoverageQuery>();
    private readonly FakeDigestRepository _digests = new();
    private readonly SequentialIdGenerator _ids = new();
    private readonly FakeClock _clock = new(Now);
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private bool _digestSavedWhenFirstPublished = true;

    public DigestAssemblerTests()
    {
        _degraded.DegradedSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DegradedSource>());

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
        new(_runs, _scope, _degraded, _digests, _ids, options ?? new DigestOptions(), _clock,
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
            reasons.Length == 0 ? ["Strong fit"] : reasons, salaryUsd);

    private static DigestCandidate Suppressed(Guid id, string reason, decimal score = 20m) =>
        new(id, score, Suppressed: true, reason, ["Below the bar"], SalaryUsd: null);

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
            new DigestCandidate(unexplained, 95m, Suppressed: false, null, [], SalaryUsd: null));

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
        GivenCandidates(new DigestCandidate(blank, 95m, Suppressed: false, null, ["   ", ""], SalaryUsd: null));

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
