using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using JobHunter.Telegram.Commands;
using JobHunter.Telegram.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// The command dispatcher (T11, contract §Commands, ADR-F10-0002). It maps the leading <c>/token</c> to a
/// registered <see cref="ICommandHandler"/>, hands the handler the remaining arguments and the chat id, and
/// returns the handler's messages. An unrecognised command gets the nearest-command suggestion, or the
/// grouped list when nothing is close — never a conversational fallback and never a call into anything
/// LLM-shaped. Both the suggestion and the fallback are derived from the same descriptor catalogue the
/// router dispatches on, so neither can drift from what the router actually accepts.
/// </summary>
public sealed class CommandRouterTests
{
    private const long OwnerChat = 4242;

    // The unknown-command reply is projected from these descriptors: a near typo of a name suggests it, and
    // a token far from all of them falls back to the grouped list.
    private static readonly IReadOnlyList<CommandDescriptor> Catalogue =
    [
        new("start", "Confirm this chat", [],
            CommandCapability.Standard, CommandGroup.Meta, false, "/start"),
        new("help", "Show the command list", [],
            CommandCapability.Standard, CommandGroup.Meta, false, "/help"),
        new("pipeline", "Applications by status", [],
            CommandCapability.Standard, CommandGroup.Pipeline, false, "/pipeline"),
    ];

    private static CommandRouter Build(out RecordingCommandHandler start, out RecordingCommandHandler help)
    {
        start = new RecordingCommandHandler("start-reply");
        help = new RecordingCommandHandler("help-reply");
        var registrations = new[]
        {
            new CommandRegistration("/start", "Confirm this chat", start),
            new CommandRegistration("/help", "Show the command list", help),
        };
        return new CommandRouter(registrations, Catalogue, NullLogger<CommandRouter>.Instance);
    }

    [Fact]
    public async Task It_dispatches_to_the_handler_registered_for_the_leading_token()
    {
        var router = Build(out var start, out _);

        var messages = await router.RouteAsync(OwnerChat, "/start", CancellationToken.None);

        start.Calls.ShouldBe(1);
        messages.ShouldHaveSingleItem().Text.ShouldBe("start-reply");
    }

    [Fact]
    public async Task It_carries_the_chat_id_and_splits_the_arguments_off_the_token()
    {
        var router = Build(out var start, out _);

        await router.RouteAsync(OwnerChat, "/start extra words", CancellationToken.None);

        start.Received.ShouldNotBeNull();
        start.Received!.ChatId.ShouldBe(OwnerChat);
        start.Received.Arguments.ShouldBe("extra words");
    }

    [Fact]
    public async Task A_bare_token_yields_null_arguments()
    {
        var router = Build(out var start, out _);

        await router.RouteAsync(OwnerChat, "/start", CancellationToken.None);

        start.Received!.Arguments.ShouldBeNull();
    }

    [Fact]
    public async Task Dispatch_is_case_insensitive_on_the_token()
    {
        var router = Build(out var start, out _);

        await router.RouteAsync(OwnerChat, "/START", CancellationToken.None);

        start.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_token_with_a_bot_mention_suffix_still_dispatches()
    {
        // Telegram appends @BotName to commands in groups; the router strips it before matching.
        var router = Build(out var start, out _);

        await router.RouteAsync(OwnerChat, "/start@JobHunterBot", CancellationToken.None);

        start.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_command_far_from_all_suggests_nothing_and_lists_the_groups()
    {
        var router = Build(out var start, out var help);

        var messages = await router.RouteAsync(OwnerChat, "/frobnicate", CancellationToken.None);

        start.Calls.ShouldBe(0);
        help.Calls.ShouldBe(0);
        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("Unknown command", Case.Insensitive);
        text.ShouldNotContain("Did you mean");
        // The grouped list is derived from the catalogue, so its commands appear.
        text.ShouldContain("/pipeline");
    }

    [Fact]
    public async Task A_near_typo_suggests_the_nearest_command_and_calls_no_handler()
    {
        var router = Build(out var start, out var help);

        var messages = await router.RouteAsync(OwnerChat, "/pipline", CancellationToken.None);

        start.Calls.ShouldBe(0);
        help.Calls.ShouldBe(0);
        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("Did you mean");
        text.ShouldContain("/pipeline");
    }

    [Fact]
    public async Task A_non_command_message_is_treated_as_unknown()
    {
        var router = Build(out _, out _);

        var messages = await router.RouteAsync(OwnerChat, "just chatting", CancellationToken.None);

        messages.ShouldHaveSingleItem().Text.ShouldContain("Unknown command", Case.Insensitive);
    }

    [Fact]
    public async Task An_empty_message_is_treated_as_unknown()
    {
        var router = Build(out _, out _);

        var messages = await router.RouteAsync(OwnerChat, "   ", CancellationToken.None);

        messages.ShouldHaveSingleItem().Text.ShouldContain("Unknown command", Case.Insensitive);
    }

    [Fact]
    public void A_null_registration_set_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() =>
            new CommandRouter(null!, Catalogue, NullLogger<CommandRouter>.Instance));
    }

    [Fact]
    public void A_null_catalogue_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() =>
            new CommandRouter([], null!, NullLogger<CommandRouter>.Instance));
    }

    [Fact]
    public void A_null_logger_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() =>
            new CommandRouter([], Catalogue, null!));
    }

    [Fact]
    public async Task A_null_message_text_is_rejected()
    {
        var router = Build(out _, out _);

        await Should.ThrowAsync<ArgumentNullException>(() =>
            router.RouteAsync(OwnerChat, null!, CancellationToken.None));
    }
}
