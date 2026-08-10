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
/// F5 T03/T04/T07: the arithmetic and restoration arms of digest assembly that the primary suite in
/// <see cref="DigestAssemblerTests"/> does not reach. The average-salary mean only ever counts a <em>shown</em>
/// candidate whose USD figure is strictly positive — a suppressed salary, a zero and a null are each excluded —
/// so the number the Owner reads is built from real, comparable pay only. And a restored card can still be
/// dropped by apply-link verification, in which case the digest states the <em>realised</em> restored count,
/// never the intended-but-unrealised one. Every collaborator is faked, so these stay zero-database,
/// zero-network unit tests.
/// </summary>
public sealed class DigestAssemblerBranchTests
{
    private static readonly DateTimeOffset RunStart = new(2026, 8, 4, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 6, 0, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");

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

    public DigestAssemblerBranchTests()
    {
        _verifier.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApplyLinkStatus.Reachable);
        _degraded.DegradedSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DegradedSource>());
        _narrative.SynthesizeAsync(Arg.Any<Guid>(), Arg.Any<NarrativeInput>(), Arg.Any<CancellationToken>())
            .Returns(call => NarrativeResult.Template(NarrativeTemplate.Render((NarrativeInput)call[1]!)));
    }

    private DigestAssembler CreateHandler(DigestOptions? options = null)
    {
        var learning = Substitute.For<ILearningSwitch>();
        learning.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        return new(_runs, _scope, _degraded, _activeCompanies, _digests, _verifier, _narrative, _ids,
            options ?? new DigestOptions(), new ApplyVerificationOptions(), learning, _clock,
            NullLogger<DigestAssembler>.Instance);
    }

    private static Run RankingCompletedRun()
    {
        var run = new Run(RunId, RunStart.AddHours(-24), RunStart, 2.00m, RunStart.AddMinutes(-5));
        run.SetScope(20);
        run.TransitionTo(RunState.Enriching, RunStart);
        run.TransitionTo(RunState.Matching, RunStart);
        run.TransitionTo(RunState.Ranking, RunStart);
        run.TransitionTo(RunState.Researching, RunStart);
        return run;
    }

    private void GivenRun(Run run) => _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);

    private void GivenCandidates(params DigestCandidate[] candidates) =>
        _scope.CandidatesAsync(RunId, Arg.Any<CancellationToken>()).Returns(candidates);

    private static string ApplyUrlFor(Guid id) => $"https://apply.example.com/{id:N}";

    private static DigestCandidate Shown(Guid id, decimal score, decimal? salaryUsd = null) =>
        new(id, score, Suppressed: false, SuppressionReason: null, ["Strong fit"], salaryUsd, ApplyUrlFor(id));

    private static DigestCandidate SuppressedWithSalary(Guid id, decimal salaryUsd) =>
        new(id, 20m, Suppressed: true, "Below the bar", ["Below the bar"], salaryUsd, ApplyUrlFor(id));

    private static DigestCandidate SuppressedRestorable(Guid id, decimal score) =>
        new(id, score, Suppressed: true, "Below your salary floor", ["Still a plausible fit"],
            SalaryUsd: null, ApplyUrlFor(id));

    private static RankingCompleted Message() =>
        new(RunId, RankedCount: 3, SuppressedCount: 0, TopJobIds: [], Now);

    private void GivenApplyLink(Guid jobId, ApplyLinkStatus status) =>
        _verifier.VerifyAsync(ApplyUrlFor(jobId), Arg.Any<CancellationToken>()).Returns(status);

    // ---- average salary: only positive, shown USD figures count -------------------------------

    [Fact]
    public async Task A_suppressed_salary_is_excluded_from_the_average_even_when_it_would_reach_the_minimum()
    {
        GivenRun(RankingCompletedRun());
        // Three USD figures total, but one belongs to a suppressed candidate — so only two shown remain, which
        // is below the minimum-of-three, so the average is null. The suppressed pay is never averaged in.
        GivenCandidates(
            Shown(Guid.CreateVersion7(), 90m, 100000m),
            Shown(Guid.CreateVersion7(), 85m, 120000m),
            SuppressedWithSalary(Guid.CreateVersion7(), 500000m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _digests.Saved.ShouldHaveSingleItem().AvgSalaryUsd.ShouldBeNull();
    }

    [Fact]
    public async Task A_zero_or_negative_salary_does_not_count_toward_the_average()
    {
        GivenRun(RankingCompletedRun());
        // Two real positive figures plus a zero and a negative: the non-positive ones are filtered out by the
        // "is > 0m" guard, leaving two — below the minimum — so the average stays null rather than skewed to 0.
        GivenCandidates(
            Shown(Guid.CreateVersion7(), 90m, 100000m),
            Shown(Guid.CreateVersion7(), 85m, 120000m),
            Shown(Guid.CreateVersion7(), 80m, 0m),
            Shown(Guid.CreateVersion7(), 75m, -5m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _digests.Saved.ShouldHaveSingleItem().AvgSalaryUsd.ShouldBeNull();
    }

    [Fact]
    public async Task The_average_rounds_the_mean_of_the_positive_shown_usd_figures_away_from_zero()
    {
        GivenRun(RankingCompletedRun());
        // 100000, 120001, 120000 → mean 113333.6667 → rounds away from zero to 113333.67.
        GivenCandidates(
            Shown(Guid.CreateVersion7(), 90m, 100000m),
            Shown(Guid.CreateVersion7(), 85m, 120001m),
            Shown(Guid.CreateVersion7(), 80m, 120000m));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _digests.Saved.ShouldHaveSingleItem().AvgSalaryUsd.ShouldBe(113333.67m);
    }

    // ---- restored count is the realised number, after verification may drop one ---------------

    [Fact]
    public async Task A_restored_card_dropped_by_verification_is_not_counted_as_restored()
    {
        GivenRun(RankingCompletedRun());
        var shown = Guid.CreateVersion7();
        var restoredLive = Guid.CreateVersion7();
        var restoredDead = Guid.CreateVersion7();
        // One shown card and two restorable suppressed ones; the floor of three intends to restore both, but
        // one restored card's apply link is confirmed dead and drops out — so the digest restored two but only
        // one reached a card. The stated count must be the realised one, not the intended two.
        GivenCandidates(
            Shown(shown, 90m),
            SuppressedRestorable(restoredLive, 38m),
            SuppressedRestorable(restoredDead, 30m));
        GivenApplyLink(restoredDead, ApplyLinkStatus.ConfirmedUnreachable);

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var digest = _digests.Saved.ShouldHaveSingleItem();
        digest.Cards.Select(c => c.JobId).ShouldBe([shown, restoredLive]);
        digest.RestoredCount.ShouldBe(1);
        // The dead restored job is flagged for the lifecycle sweep like any other unreachable card.
        Publishes().OfType<ApplyDestinationUnreachable>().ShouldHaveSingleItem().JobId.ShouldBe(restoredDead);
    }

    private List<object> Publishes() =>
        _bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IMessageBus.PublishAsync))
            .Select(c => c.GetArguments())
            .Where(a => a.Length > 0 && a[0] is not null)
            .Select(a => a[0]!)
            .ToList();

    private sealed class FakeDigestRepository : IDigestRepository
    {
        private Digest? _staged;

        public List<Digest> Saved { get; } = [];

        public void Add(Digest digest) => _staged = digest;

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
