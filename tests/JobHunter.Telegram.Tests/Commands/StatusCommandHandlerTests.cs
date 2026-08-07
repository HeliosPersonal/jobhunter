using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Sources;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/status</c> (catalogue §Operations · Sensitive · read): the first question R1 asks — last Run's
/// outcome, its cost against the ceiling, the discovered→matched→delivered·hidden counts, and any degraded
/// sources (AC-06). It resolves the day's Run exactly as delivery and <c>/digest</c> do (the live one, else
/// the most recent, including a terminal Run — the degraded day that still owes an answer), reads the four
/// counts from the persisted <see cref="Digest"/> (the authoritative artifact) and the degraded sources from
/// <see cref="IDegradedCoverageQuery"/> as of the injected clock. Read-only: no LLM, no write, no CV. With no
/// Run yet it says so plainly rather than inventing a status.
/// </summary>
public sealed class StatusCommandHandlerTests
{
    private const long OwnerChat = 4242;
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IDigestRepository _digests = Substitute.For<IDigestRepository>();
    private readonly IDegradedCoverageQuery _degraded = Substitute.For<IDegradedCoverageQuery>();
    private readonly FakeClock _clock = new(Now);

    private StatusCommandHandler NewHandler() =>
        new(_runs, _digests, _degraded, _clock, NullLogger<StatusCommandHandler>.Instance);

    private void NoDegradedSources() =>
        _degraded.DegradedSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DegradedSource>());

    [Fact]
    public async Task It_reports_state_cost_against_ceiling_and_the_four_counts()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns(DeliveredRun());
        _digests.FindByRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(DigestWith(
            totalNewJobs: 127, strongMatches: 51, delivered: 9, suppressed: 34));
        NoDegradedSources();

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))
            .ShouldHaveSingleItem().Text;

        text.ShouldContain("Delivered");
        // Cost against ceiling: both figures. The decimal point is MarkdownV2-escaped, so it reads "1\.04".
        text.ShouldContain(@"1\.04");
        text.ShouldContain(@"2\.00");
        // The count line: discovered → matched → delivered · hidden.
        text.ShouldContain("127");
        text.ShouldContain("51");
        text.ShouldContain("9");
        text.ShouldContain("34");
    }

    [Fact]
    public async Task It_prefers_the_live_run_over_the_most_recent_one()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns(DeliveredRun());
        _digests.FindByRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(DigestWith(1, 1, 1, 0));
        NoDegradedSources();

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // The most-recent fallback is only consulted when there is no live Run.
        await _runs.DidNotReceive().FindMostRecentRunAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_names_the_quarantined_sources_as_of_the_clock()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns(DeliveredRun());
        _digests.FindByRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(DigestWith(1, 1, 1, 0));
        _degraded.DegradedSourcesAsync(Now, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new DegradedSource(Guid.NewGuid(), Guid.NewGuid(), "Acme", "greenhouse", 3, Now.AddHours(6)),
        });

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))
            .ShouldHaveSingleItem().Text;

        text.ShouldContain("Acme");
        text.ShouldContain("quarantined", Case.Insensitive);
        await _degraded.Received(1).DegradedSourcesAsync(Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task With_no_degraded_source_it_says_all_sources_healthy_rather_than_nothing()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns(DeliveredRun());
        _digests.FindByRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(DigestWith(1, 1, 1, 0));
        NoDegradedSources();

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))
            .ShouldHaveSingleItem().Text;

        text.ShouldContain("healthy", Case.Insensitive);
    }

    [Fact]
    public async Task No_run_yields_a_plain_nothing_yet_message()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))
            .ShouldHaveSingleItem().Text;

        text.ShouldContain("no run", Case.Insensitive);
        await _degraded.DidNotReceive().DegradedSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_run_without_a_persisted_digest_reports_the_run_state_but_no_counts()
    {
        // The pipeline is mid-flight: a live Run exists but its digest is not assembled yet. The status still
        // answers with the Run's state rather than a bare "nothing", but has no counts to show.
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns(EnrichingRun());
        _digests.FindByRunAsync(RunId, Arg.Any<CancellationToken>()).Returns((Digest?)null);
        NoDegradedSources();

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))
            .ShouldHaveSingleItem().Text;

        text.ShouldContain("Enriching");
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new StatusCommandHandler(null!, _digests, _degraded, _clock, NullLogger<StatusCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new StatusCommandHandler(_runs, null!, _degraded, _clock, NullLogger<StatusCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new StatusCommandHandler(_runs, _digests, null!, _clock, NullLogger<StatusCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new StatusCommandHandler(_runs, _digests, _degraded, null!, NullLogger<StatusCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new StatusCommandHandler(_runs, _digests, _degraded, _clock, null!));
    }

    private static Run DeliveredRun()
    {
        var run = new Run(RunId, Now.AddDays(-1), Now.AddHours(-2), ceilingUsd: 2.00m, Now.AddHours(-6));
        run.SetSpend(1.04m);
        run.SetScope(127);
        run.TransitionTo(RunState.Enriching, Now.AddHours(-5));
        run.TransitionTo(RunState.Matching, Now.AddHours(-4));
        run.TransitionTo(RunState.Ranking, Now.AddHours(-4));
        run.TransitionTo(RunState.Researching, Now.AddHours(-3));
        run.TransitionTo(RunState.Reporting, Now.AddHours(-2));
        run.TransitionTo(RunState.Delivered, Now.AddHours(-1));
        return run;
    }

    private static Run EnrichingRun()
    {
        var run = new Run(RunId, Now.AddDays(-1), Now, ceilingUsd: 2.00m, Now.AddMinutes(-30));
        run.SetSpend(0.10m);
        run.TransitionTo(RunState.Enriching, Now.AddMinutes(-20));
        return run;
    }

    private static Digest DigestWith(int totalNewJobs, int strongMatches, int delivered, int suppressed)
    {
        var digestId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var cards = Enumerable.Range(0, delivered)
            .Select(rank => new DigestCard(
                Guid.NewGuid(), digestId, Guid.NewGuid(), RunId, rank + 1, score: 90m,
                reasons: ["reason"], applyUrlVerified: true))
            .ToList();
        var breakdown = suppressed == 0
            ? Array.Empty<SuppressionTally>()
            : [SuppressionTally.TryCreate("below floor", suppressed).Value];
        return new Digest(
            digestId, RunId, DigestMode.Full, totalNewJobs, strongMatches, avgSalaryUsd: null,
            suppressedCount: suppressed, suppressionBreakdown: breakdown, carriedOverCount: 0,
            companiesChecked: 40, analysedCount: 0, degradedSources: [], narrative: null,
            NarrativeSource.Template, promptVersion: null, cards: cards, generatedAt: Now.AddHours(-2));
    }
}
