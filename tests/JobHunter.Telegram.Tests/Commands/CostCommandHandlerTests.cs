using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/cost [month]</c> (catalogue §Operations · Sensitive · read): the calendar month's spend broken down by
/// pipeline stage and model tier, each line carrying the estimated and the actual dollars, flagging estimate-vs-actual
/// drift above 20% — how a stale pricing table surfaces. It reads <see cref="IMonthlyCostQuery"/> for a month resolved
/// from the optional <c>YYYY-MM</c> argument, defaulting to the current month from the injected clock. Read-only: no
/// LLM, no write, no CV. Every value reaches the reply through the one MarkdownV2 escaper.
/// </summary>
public sealed class CostCommandHandlerTests
{
    private const long OwnerChat = 4242;
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly IMonthlyCostQuery _cost = Substitute.For<IMonthlyCostQuery>();
    private readonly FakeClock _clock = new(Now);

    private CostCommandHandler NewHandler() =>
        new(_cost, _clock, NullLogger<CostCommandHandler>.Instance);

    private void CostReturns(params CostBreakdownRow[] rows) =>
        _cost.BreakdownForMonthAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(rows);

    [Fact]
    public async Task It_breaks_the_month_down_by_stage_and_tier()
    {
        CostReturns(
            new CostBreakdownRow("Enrichment", "Cheap", EstimatedUsd: 0.15m, ActualUsd: 0.16m),
            new CostBreakdownRow("Matching", "Deep", EstimatedUsd: 1.00m, ActualUsd: 1.05m));

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldContain("Enrichment");
        text.ShouldContain("Cheap");
        text.ShouldContain("Matching");
        text.ShouldContain("Deep");
    }

    [Fact]
    public async Task It_defaults_to_the_current_month_from_the_clock()
    {
        CostReturns(new CostBreakdownRow("Enrichment", "Cheap", 0.10m, 0.10m));

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // The first instant of the clock's month — August 2026 — at UTC.
        await _cost.Received(1).BreakdownForMonthAsync(
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_reads_the_month_named_in_the_argument()
    {
        CostReturns(new CostBreakdownRow("Enrichment", "Cheap", 0.10m, 0.10m));

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "2026-05"));

        await _cost.Received(1).BreakdownForMonthAsync(
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_flags_a_line_whose_actual_drifts_more_than_twenty_percent_above_the_estimate()
    {
        // Estimate 1.00, actual 1.30 — 30% over, beyond the 20% band a stale pricing table would surface.
        CostReturns(new CostBreakdownRow("Matching", "Deep", EstimatedUsd: 1.00m, ActualUsd: 1.30m));

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldContain("⚠️");
    }

    [Fact]
    public async Task A_line_within_the_twenty_percent_band_is_not_flagged()
    {
        // Estimate 1.00, actual 1.20 — exactly 20%, within the band, so no drift warning.
        CostReturns(new CostBreakdownRow("Matching", "Deep", EstimatedUsd: 1.00m, ActualUsd: 1.20m));

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldNotContain("⚠️");
    }

    [Fact]
    public async Task A_zero_estimate_with_a_real_actual_is_flagged_rather_than_dividing_by_zero()
    {
        // No estimate was booked but dollars were spent — that is unbounded drift, and must not divide by zero.
        CostReturns(new CostBreakdownRow("Research", "Deep", EstimatedUsd: 0m, ActualUsd: 0.40m));

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldContain("⚠️");
    }

    [Fact]
    public async Task With_no_spend_in_the_month_it_says_so_plainly()
    {
        CostReturns();

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldContain("No", Case.Insensitive);
    }

    [Fact]
    public async Task An_unparseable_month_argument_yields_a_usage_line_and_reads_nothing()
    {
        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "not-a-month"));

        messages[0].Text.ShouldContain("YYYY", Case.Insensitive);
        await _cost.DidNotReceive().BreakdownForMonthAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new CostCommandHandler(null!, _clock, NullLogger<CostCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new CostCommandHandler(_cost, null!, NullLogger<CostCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new CostCommandHandler(_cost, _clock, null!));
    }
}
