using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/hidden</c> (contract §Digest and discovery, F7 T08 done-when 6, invariant 11): what the latest Run
/// suppressed, grouped by the reason it was withheld, each job in the same scannable card layout as the
/// digest so the Owner can see what the learned model hid and open one — making suppression regret
/// measurable rather than silent. F7 owns this handler; F10 only registers it (catalogue ownership table).
/// It reads through <see cref="IHiddenJobsQuery"/> and never touches the CV (the CV crosses exactly one
/// boundary, not this one). An empty result is one plain, helpful line, never an empty message.
/// </summary>
public sealed class HiddenCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private readonly IHiddenJobsQuery _hidden = Substitute.For<IHiddenJobsQuery>();

    private HiddenCommandHandler NewHandler() => new(_hidden, NullLogger<HiddenCommandHandler>.Instance);

    private static HiddenJob Job(string title, decimal score, string reason) =>
        new(Guid.NewGuid(), title, "Acme", score, reason);

    [Fact]
    public async Task It_groups_the_hidden_jobs_by_reason_with_a_bold_header_and_count()
    {
        _hidden.HiddenAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
        [
            Job("Staff SRE", 42m, "Below salary floor"),
            Job("Principal Engineer", 40m, "Below salary floor"),
            Job("Backend Engineer", 30m, "Timezone incompatible"),
        ]);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // A header per reason with the count of jobs it withheld, in first-seen order.
        messages[0].Text.ShouldContain("Below salary floor");
        messages[0].Text.ShouldContain("2");
        messages[1].Text.ShouldContain("Staff SRE");
        messages[2].Text.ShouldContain("Principal Engineer");
        messages[3].Text.ShouldContain("Timezone incompatible");
        messages[3].Text.ShouldContain("1");
        messages[4].Text.ShouldContain("Backend Engineer");
    }

    [Fact]
    public async Task It_shows_each_hidden_jobs_reason_on_its_card()
    {
        _hidden.HiddenAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Job("Staff SRE", 42m, "Below salary floor")]);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // The card carries the suppression reason as its evidence line, so the "why" travels with the job.
        messages[^1].Text.ShouldContain("Below salary floor");
    }

    [Fact]
    public async Task An_empty_result_yields_one_plain_helpful_line()
    {
        _hidden.HiddenAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("hidden", Case.Insensitive);
    }

    [Fact]
    public async Task It_asks_for_a_bounded_page_never_the_whole_history()
    {
        _hidden.HiddenAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        await _hidden.Received(1).HiddenAsync(Arg.Is<int>(limit => limit > 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new HiddenCommandHandler(null!, NullLogger<HiddenCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new HiddenCommandHandler(_hidden, null!));
    }
}
