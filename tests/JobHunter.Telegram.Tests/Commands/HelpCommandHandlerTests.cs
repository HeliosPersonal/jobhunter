using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using JobHunter.Telegram.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/help</c> (contract §Meta, AC-09): the grouped command list, served from the same descriptor list the
/// router dispatches on, so the help a user reads is exactly the surface and cannot drift from it. With a
/// command argument it serves that command's detailed usage; with an unknown one it falls back to the grouped
/// list rather than an error, so a mistyped <c>/help x</c> still helps.
/// </summary>
public sealed class HelpCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private static readonly IReadOnlyList<CommandDescriptor> Commands =
    [
        new("digest", "Re-read today's digest.", [],
            CommandCapability.Standard, CommandGroup.DigestAndDiscovery, false, "/digest"),
        new("more", "The next cards below the cut.",
            [new ArgumentSpec("count", required: false, "How many cards to show.")],
            CommandCapability.Standard, CommandGroup.DigestAndDiscovery, false, "/more", example: "/more 10"),
        new("run", "Trigger the daily pipeline.", [],
            CommandCapability.Sensitive, CommandGroup.Operations, true, "/run", "Start it?"),
    ];

    private static HelpCommandHandler Build() => new(Commands);

    [Fact]
    public async Task Without_an_argument_it_lists_the_commands_grouped_by_section()
    {
        var messages = await Build().HandleAsync(new CommandRequest(OwnerChat, null));

        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("*Digest and discovery*");
        text.ShouldContain("*Operations*");
        text.ShouldContain("/digest");
        text.ShouldContain("/run");
    }

    [Fact]
    public async Task With_a_command_argument_it_serves_that_commands_usage()
    {
        var messages = await Build().HandleAsync(new CommandRequest(OwnerChat, "more"));

        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("*/more \\[count\\]*");
        text.ShouldContain("/more 10");
    }

    [Fact]
    public async Task A_leading_slash_on_the_argument_is_tolerated()
    {
        var messages = await Build().HandleAsync(new CommandRequest(OwnerChat, "/more"));

        messages.ShouldHaveSingleItem().Text.ShouldContain("*/more \\[count\\]*");
    }

    [Fact]
    public async Task An_unknown_command_argument_falls_back_to_the_grouped_list()
    {
        var messages = await Build().HandleAsync(new CommandRequest(OwnerChat, "nonesuch"));

        messages.ShouldHaveSingleItem().Text.ShouldContain("*Digest and discovery*");
    }

    [Fact]
    public void A_null_command_list_is_rejected() =>
        Should.Throw<ArgumentNullException>(() => new HelpCommandHandler(null!));

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        var handler = Build();

        await Should.ThrowAsync<ArgumentNullException>(() => handler.HandleAsync(null!));
    }
}
