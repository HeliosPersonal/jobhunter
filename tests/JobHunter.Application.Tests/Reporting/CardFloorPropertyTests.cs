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
/// F7 T09 (done-when 3, QG-3): the card floor as a <em>property</em> over generated adversarial candidate
/// distributions, the display-time counterpart to <see cref="Ranking"/>'s dimension-bound property suite
/// (<c>WeightFitterBoundingTests</c>). The T07 <c>DigestAssembler</c> tests already pin the floor on a handful
/// of hand-built cases; this asserts the same guarantee holds whatever the shape of the day's scores —
/// however aggressively learning suppressed, the digest is never emptied below the floor while candidates
/// carrying a reason remain, and the suppressed count always reconciles to the raw suppressed rows
/// (invariant 11), because restoration is display-only and never a re-score.
///
/// <para>The distributions are deterministic, varied by an <c>[InlineData]</c> seed and a seeded
/// <see cref="Random"/> — no RNG library, so a failure is reproducible from its seed (the codebase convention,
/// same as the synthetic corpus). Every apply link is reachable, so verification never drops a card and the
/// floor property is isolated from link health.</para>
/// </summary>
public sealed class CardFloorPropertyTests
{
    private static readonly DateTimeOffset RunStart = new(2026, 8, 4, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 6, 0, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");

    private static readonly DigestOptions Options = new();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Over_adversarial_score_distributions_the_floor_holds_and_the_suppressed_count_reconciles(int seed)
    {
        var random = new Random(seed);

        // A day's worth of scores skewed towards suppression: a handful might clear the card bar, but most are
        // suppressed with a reason — exactly the shape that would empty the digest without the floor.
        var shownQualifying = random.Next(0, 4);      // 0..3 clear the 70 card bar
        var restorable = random.Next(0, 12);          // 0..11 suppressed, each with a reason
        var reasonless = random.Next(0, 3);           // 0..2 suppressed with no usable reason — never restorable

        var candidates = new List<DigestCandidate>();
        for (var i = 0; i < shownQualifying; i++)
        {
            candidates.Add(Shown(70m + random.Next(0, 30)));
        }

        for (var i = 0; i < restorable; i++)
        {
            candidates.Add(Suppressed("Below your salary floor", score: random.Next(1, 40), withReason: true));
        }

        for (var i = 0; i < reasonless; i++)
        {
            candidates.Add(Suppressed("Below the bar", score: random.Next(1, 40), withReason: false));
        }

        Shuffle(candidates, random);

        var digests = new FakeDigestRepository();
        await CreateHandler(digests, candidates).Handle(Message(), Substitute.For<IMessageBus>(), CancellationToken.None);

        var digest = digests.Saved.ShouldHaveSingleItem();

        // The floor guarantee: the digest is never emptied below MinCards while enough candidates carrying a
        // reason exist. When the reasoned pool is too thin to reach the floor, the digest holds exactly what it
        // could honestly show — the floor cannot manufacture an explanation (invariant 4).
        var reachableFloor = Math.Min(Options.MinCards, shownQualifying + restorable);
        digest.Cards.Count.ShouldBeGreaterThanOrEqualTo(reachableFloor);

        // Never more than the cap, whatever the distribution.
        digest.Cards.Count.ShouldBeLessThanOrEqualTo(Options.MaxCards);

        // Restoration is display-only: the suppressed count is the raw suppressed rows — reasoned or not,
        // restored or not — so the footer still reconciles to the database (invariant 11 / QG-2).
        var suppressedRows = restorable + reasonless;
        digest.SuppressedCount.ShouldBe(suppressedRows);
        digest.SuppressionBreakdown.Sum(t => t.Count).ShouldBe(suppressedRows);

        // RestoredCount is only ever the shortfall the floor had to make up from the reasoned pool, never more
        // than the floor and never more than the pool held.
        digest.RestoredCount.ShouldBeLessThanOrEqualTo(Options.MinCards);
        digest.RestoredCount.ShouldBeLessThanOrEqualTo(restorable);
        digest.RestoredCount.ShouldBe(Math.Max(0, digest.Cards.Count - shownQualifying));
    }

    private static void Shuffle(List<DigestCandidate> list, Random random)
    {
        // A seeded Fisher–Yates so the input order is itself adversarial — the assembler must not depend on the
        // query happening to hand it shown candidates first.
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static DigestCandidate Shown(decimal score) =>
        new(Guid.CreateVersion7(), score, Suppressed: false, SuppressionReason: null, ["Strong fit"],
            SalaryUsd: null, Apply());

    private static DigestCandidate Suppressed(string reason, int score, bool withReason)
    {
        var id = Guid.CreateVersion7();
        // A suppressed row always carries a suppression reason (the Score invariant); "withReason" toggles the
        // match reasons the card would show — a blank one cannot be restored (invariant 4).
        return new(id, score, Suppressed: true, reason, withReason ? ["Still a plausible fit"] : ["   "],
            SalaryUsd: null, Apply());
    }

    private static string Apply() => $"https://apply.example.com/{Guid.NewGuid():N}";

    private static RankingCompleted Message() =>
        new(RunId, RankedCount: 0, SuppressedCount: 0, TopJobIds: [], Now);

    private static DigestAssembler CreateHandler(FakeDigestRepository digests, List<DigestCandidate> candidates)
    {
        var runs = Substitute.For<IRunRepository>();
        var run = new Run(RunId, RunStart.AddHours(-24), RunStart, 2.00m, RunStart.AddMinutes(-5));
        run.SetScope(candidates.Count);
        run.TransitionTo(RunState.Enriching, RunStart);
        run.TransitionTo(RunState.Matching, RunStart);
        run.TransitionTo(RunState.Ranking, RunStart);
        run.TransitionTo(RunState.Researching, RunStart);
        runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);

        var scope = Substitute.For<IDigestScopeQuery>();
        scope.CandidatesAsync(RunId, Arg.Any<CancellationToken>()).Returns(candidates);

        var degraded = Substitute.For<IDegradedCoverageQuery>();
        degraded.DegradedSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DegradedSource>());

        var verifier = Substitute.For<IApplyLinkVerifier>();
        verifier.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApplyLinkStatus.Reachable);

        var narrative = Substitute.For<INarrativeSynthesizer>();
        narrative.SynthesizeAsync(Arg.Any<Guid>(), Arg.Any<NarrativeInput>(), Arg.Any<CancellationToken>())
            .Returns(call => NarrativeResult.Template(NarrativeTemplate.Render((NarrativeInput)call[1]!)));

        var learning = Substitute.For<ILearningSwitch>();
        learning.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);

        return new DigestAssembler(
            runs, scope, degraded, Substitute.For<IActiveCompanyCountQuery>(), digests, verifier, narrative,
            new SequentialIdGenerator(), Options, new ApplyVerificationOptions(), learning, new FakeClock(Now),
            NullLogger<DigestAssembler>.Instance);
    }

    private sealed class FakeDigestRepository : IDigestRepository
    {
        private Digest? _staged;

        public List<Digest> Saved { get; } = [];

        public void Add(Digest digest) => _staged = digest;

        public Task<Digest?> FindByRunAsync(Guid runId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(d => d.RunId == runId));

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_staged is null)
            {
                return Task.FromResult(0);
            }

            Saved.Add(_staged);
            _staged = null;
            return Task.FromResult(1);
        }
    }
}
