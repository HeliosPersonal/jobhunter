using JobHunter.Telegram.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/help</c> (contract §Commands): the list of commands, served from the same registered set the router
/// dispatches on, so the help a user reads is exactly what the bot accepts and cannot drift from it.
/// </summary>
public sealed class HelpCommandHandlerTests
{
    private const long OwnerChat = 4242;

    [Fact]
    public async Task It_returns_the_routers_help_list()
    {
        const string helpList = "/start — Confirm this chat\n/help — Show the command list";
        var handler = new HelpCommandHandler(() => helpList);

        var messages = await handler.HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldBe(helpList);
    }

    [Fact]
    public void A_null_help_source_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new HelpCommandHandler(null!));
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        var handler = new HelpCommandHandler(() => "list");

        await Should.ThrowAsync<ArgumentNullException>(() => handler.HandleAsync(null!));
    }
}
