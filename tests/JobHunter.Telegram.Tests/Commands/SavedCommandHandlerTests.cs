using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/saved</c> (contract §Commands, T11 AC): the roles the Owner saved, newest-first, in the same
/// scannable card layout as the digest (AC-12). It reads the store <c>/saved</c> is built on through
/// <see cref="ISavedRolesQuery"/> and renders each role through the one shared card formatter — there is no
/// second layout. An empty history is a plain, helpful line, never an empty message.
/// </summary>
public sealed class SavedCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private readonly ISavedRolesQuery _saved = Substitute.For<ISavedRolesQuery>();

    private SavedCommandHandler NewHandler() => new(_saved, NullLogger<SavedCommandHandler>.Instance);

    private static SavedRole Role(string title, decimal score) => new(
        Guid.NewGuid(), title, "Acme", "Series B", ["Germany"], "Remote",
        SalaryMin: 150_000, SalaryMax: 180_000, SalaryCurrency: "USD", score,
        ["A strong match on Kafka."], new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task It_renders_one_card_message_per_saved_role()
    {
        _saved.SavedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Role("Staff SRE", 91m), Role("Principal Engineer", 88m)]);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.Count.ShouldBe(2);
        messages[0].Text.ShouldContain("Staff SRE");
        messages[0].Text.ShouldContain("Acme");
        messages[0].Text.ShouldContain("91");
        messages[1].Text.ShouldContain("Principal Engineer");
    }

    [Fact]
    public async Task It_shows_each_roles_reasons_the_rankings_own_explanation()
    {
        _saved.SavedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([Role("Staff SRE", 91m)]);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("strong match on Kafka");
    }

    [Fact]
    public async Task An_empty_history_yields_one_plain_helpful_line()
    {
        _saved.SavedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("saved", Case.Insensitive);
    }

    [Fact]
    public async Task It_asks_for_a_bounded_page_never_the_whole_history()
    {
        _saved.SavedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        await _saved.Received(1).SavedAsync(Arg.Is<int>(limit => limit > 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new SavedCommandHandler(null!, NullLogger<SavedCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new SavedCommandHandler(_saved, null!));
    }
}
