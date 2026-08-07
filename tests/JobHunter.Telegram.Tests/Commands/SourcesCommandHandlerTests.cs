using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Sources;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/sources</c> (catalogue §Operations · Sensitive · read): per-provider fetch health over the last 24 hours —
/// attempts, successes — and the sources currently quarantined, each with a release button (R4's main action
/// without a terminal). It composes the source-health roll-up (<see cref="ISourceHealthQuery"/>) with the
/// quarantine list (<see cref="IDegradedCoverageQuery"/>) as of the injected clock. Read-only: the release
/// <em>tap</em> is a callback wired in T10, exactly as the card-action taps are; this task renders the button.
/// Every value reaches the reply through the one MarkdownV2 escaper.
/// </summary>
public sealed class SourcesCommandHandlerTests
{
    private const long OwnerChat = 4242;
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly ISourceHealthQuery _health = Substitute.For<ISourceHealthQuery>();
    private readonly IDegradedCoverageQuery _degraded = Substitute.For<IDegradedCoverageQuery>();
    private readonly FakeClock _clock = new(Now);

    private SourcesCommandHandler NewHandler() =>
        new(_health, _degraded, _clock, NullLogger<SourcesCommandHandler>.Instance);

    private void HealthReturns(params SourceHealth[] rows) =>
        _health.HealthSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(rows);

    private void DegradedReturns(params DegradedSource[] rows) =>
        _degraded.DegradedSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(rows);

    [Fact]
    public async Task It_reports_each_providers_attempts_and_successes()
    {
        HealthReturns(
            new SourceHealth("Greenhouse", Attempts: 10, Successes: 9, Now.AddHours(-1)),
            new SourceHealth("Lever", Attempts: 4, Successes: 4, Now.AddHours(-2)));
        DegradedReturns();

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldContain("Greenhouse");
        text.ShouldContain("9");
        text.ShouldContain("10");
        text.ShouldContain("Lever");
    }

    [Fact]
    public async Task It_reads_health_over_the_trailing_24h_from_the_clock()
    {
        HealthReturns();
        DegradedReturns();

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        await _health.Received(1).HealthSinceAsync(Now.AddHours(-24), Arg.Any<CancellationToken>());
        await _degraded.Received(1).DegradedSourcesAsync(Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_quarantined_source_carries_a_release_button_naming_its_source_id()
    {
        var sourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        HealthReturns(new SourceHealth("Greenhouse", 10, 3, Now.AddHours(-1)));
        DegradedReturns(new DegradedSource(sourceId, Guid.NewGuid(), "Acme", "Greenhouse", 5, Now.AddHours(6)));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // The quarantined source is named, and a callback button carries the release payload the T10 rewire routes.
        var button = messages.SelectMany(m => m.Keyboard).SelectMany(row => row).ShouldHaveSingleItem();
        button.CallbackData.ShouldNotBeNull();
        button.CallbackData.ShouldContain(sourceId.ToString());
        messages.Any(m => m.Text.Contains("Acme")).ShouldBeTrue();
    }

    [Fact]
    public async Task With_nothing_quarantined_it_says_so_and_carries_no_button()
    {
        HealthReturns(new SourceHealth("Greenhouse", 10, 10, Now.AddHours(-1)));
        DegradedReturns();

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.SelectMany(m => m.Keyboard).SelectMany(row => row).ShouldBeEmpty();
        messages.Any(m => m.Text.Contains("quarantine", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
    }

    [Fact]
    public async Task With_no_fetch_attempt_at_all_it_says_so_plainly()
    {
        HealthReturns();
        DegradedReturns();

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldContain("No", Case.Insensitive);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new SourcesCommandHandler(null!, _degraded, _clock, NullLogger<SourcesCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new SourcesCommandHandler(_health, null!, _clock, NullLogger<SourcesCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new SourcesCommandHandler(_health, _degraded, null!, NullLogger<SourcesCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new SourcesCommandHandler(_health, _degraded, _clock, null!));
    }
}
