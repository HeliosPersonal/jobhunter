using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using JobHunter.Telegram.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/start</c> (contract §Meta). It confirms the chat id and that this chat is authorised, then appends the
/// grouped command list — the same list <c>/help</c> serves, from the same descriptors the router dispatches
/// on, so a first-time Owner sees the whole surface at once. It only ever runs for the Owner, because the
/// allowlist gate (<see cref="Auth.OwnerAuthorizer"/>) drops an unauthorised chat before any command is
/// routed, so "an unauthorised chat gets no confirmation, only a log entry" is satisfied upstream and there
/// is no unauthorised branch to test here.
/// </summary>
public sealed class StartCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private static readonly IReadOnlyList<CommandDescriptor> Commands =
    [
        new("digest", "Re-read today's digest.", [],
            CommandCapability.Standard, CommandGroup.DigestAndDiscovery, false, "/digest"),
        new("run", "Trigger the daily pipeline.", [],
            CommandCapability.Sensitive, CommandGroup.Operations, true, "/run", "Start it?"),
    ];

    private static StartCommandHandler Build() => new(Commands);

    [Fact]
    public async Task It_confirms_the_chat_is_authorised()
    {
        var messages = await Build().HandleAsync(new CommandRequest(OwnerChat, null));

        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain(OwnerChat.ToString(System.Globalization.CultureInfo.InvariantCulture));
        text.ShouldContain("authorised", Case.Insensitive);
    }

    [Fact]
    public async Task It_appends_the_grouped_command_list()
    {
        var messages = await Build().HandleAsync(new CommandRequest(OwnerChat, null));

        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("*Digest and discovery*");
        text.ShouldContain("/digest");
        text.ShouldContain("/run");
    }

    [Fact]
    public void A_null_command_list_is_rejected() =>
        Should.Throw<ArgumentNullException>(() => new StartCommandHandler(null!));

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        var handler = Build();

        await Should.ThrowAsync<ArgumentNullException>(() => handler.HandleAsync(null!));
    }
}
