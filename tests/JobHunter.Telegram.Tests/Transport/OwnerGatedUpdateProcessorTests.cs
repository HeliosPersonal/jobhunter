using JobHunter.Telegram.Auth;
using JobHunter.Telegram.Tests.Support;
using JobHunter.Telegram.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Transport;

/// <summary>
/// The allowlist gate at the front of update processing (AC-10) and the callback routing behind it (T10).
/// An update from an unauthorised chat, or one with no chat at all, is dropped before routing; only the
/// Owner's update passes, and an authorised callback query is routed to the <see cref="ICallbackRouter"/>
/// while an authorised message is not (message routing is T11).
/// </summary>
public sealed class OwnerGatedUpdateProcessorTests
{
    private const long OwnerChat = 4242;

    private static OwnerGatedUpdateProcessor Build(out CapturingLogger<OwnerAuthorizer> authLog, out RecordingCallbackRouter router)
        => Build(out authLog, out router, out _);

    private static OwnerGatedUpdateProcessor Build(
        out CapturingLogger<OwnerAuthorizer> authLog,
        out RecordingCallbackRouter router,
        out RecordingCommandDispatcher commands)
    {
        var options = Options.Create(new TelegramOptions { BotToken = "t", AllowedChatIds = [OwnerChat] });
        authLog = new CapturingLogger<OwnerAuthorizer>();
        var authorizer = new OwnerAuthorizer(options, authLog);
        router = new RecordingCallbackRouter();
        commands = new RecordingCommandDispatcher();
        return new OwnerGatedUpdateProcessor(authorizer, router, commands, NullLogger<OwnerGatedUpdateProcessor>.Instance);
    }

    private static TelegramUpdate MessageFrom(long chatId, long updateId = 1, string text = "/digest") =>
        new(updateId, new TelegramMessage(new TelegramChat(chatId), text), null);

    private static TelegramUpdate CallbackFrom(long chatId, long updateId = 1) =>
        new(updateId, null, new TelegramCallbackQuery("cb1", "ign:ab12", new TelegramMessage(new TelegramChat(chatId), null)));

    [Fact]
    public async Task An_update_from_an_unauthorised_chat_is_dropped_and_logged()
    {
        var processor = Build(out var authLog, out _);

        await processor.ProcessAsync(MessageFrom(9999));

        authLog.Entries.ShouldContain(e => e.Message.Contains("9999", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_owners_message_passes_the_gate()
    {
        var processor = Build(out var authLog, out _);

        await processor.ProcessAsync(MessageFrom(OwnerChat));

        authLog.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_owners_callback_query_passes_the_gate()
    {
        var processor = Build(out var authLog, out _);

        await processor.ProcessAsync(CallbackFrom(OwnerChat));

        authLog.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_owners_callback_query_is_routed_to_the_callback_router()
    {
        var processor = Build(out _, out var router);
        var update = CallbackFrom(OwnerChat);

        await processor.ProcessAsync(update);

        var routed = router.Routed.ShouldHaveSingleItem();
        routed.ShouldBe(update.CallbackQuery);
    }

    [Fact]
    public async Task An_authorised_message_without_a_callback_is_not_routed_to_the_callback_router()
    {
        var processor = Build(out _, out var router);

        await processor.ProcessAsync(MessageFrom(OwnerChat));

        router.Routed.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_owners_slash_command_message_is_dispatched_to_the_command_path()
    {
        var processor = Build(out _, out _, out var commands);

        await processor.ProcessAsync(MessageFrom(OwnerChat, text: "/saved"));

        var dispatched = commands.Dispatched.ShouldHaveSingleItem();
        dispatched.ChatId.ShouldBe(OwnerChat);
        dispatched.Text.ShouldBe("/saved");
    }

    [Fact]
    public async Task A_leading_and_trailing_whitespace_slash_command_is_still_dispatched()
    {
        var processor = Build(out _, out _, out var commands);

        await processor.ProcessAsync(MessageFrom(OwnerChat, text: "  /help  "));

        commands.Dispatched.ShouldHaveSingleItem().Text.ShouldBe("  /help  ");
    }

    [Fact]
    public async Task An_owners_non_command_message_is_not_dispatched()
    {
        var processor = Build(out _, out _, out var commands);

        await processor.ProcessAsync(MessageFrom(OwnerChat, text: "hello there"));

        commands.Dispatched.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_owners_message_with_no_text_is_not_dispatched()
    {
        var processor = Build(out _, out _, out var commands);
        var update = new TelegramUpdate(1, new TelegramMessage(new TelegramChat(OwnerChat), null), null);

        await processor.ProcessAsync(update);

        commands.Dispatched.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unauthorised_slash_command_is_not_dispatched()
    {
        var processor = Build(out _, out _, out var commands);

        await processor.ProcessAsync(MessageFrom(9999, text: "/saved"));

        commands.Dispatched.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unauthorised_callback_query_is_not_routed()
    {
        var processor = Build(out _, out var router);

        await processor.ProcessAsync(CallbackFrom(9999));

        router.Routed.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_update_with_no_chat_is_dropped()
    {
        var processor = Build(out _, out _);
        var chatless = new TelegramUpdate(1, null, null);

        // No chat means not the Owner's — dropped silently, not routed and not thrown.
        await Should.NotThrowAsync(() => processor.ProcessAsync(chatless));
    }

    [Fact]
    public async Task A_null_update_is_rejected()
    {
        var processor = Build(out _, out _);

        await Should.ThrowAsync<ArgumentNullException>(() => processor.ProcessAsync(null!));
    }

    [Fact]
    public async Task An_authorised_callback_query_without_a_message_chat_is_dropped()
    {
        var processor = Build(out _, out var router);
        // A callback with no message (so no chat) resolves to no chat id — treated as not the Owner's.
        var chatless = new TelegramUpdate(1, null, new TelegramCallbackQuery("cb1", "x", null));

        await Should.NotThrowAsync(() => processor.ProcessAsync(chatless));
        router.Routed.ShouldBeEmpty();
    }

    [Fact]
    public void A_null_authorizer_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() =>
            new OwnerGatedUpdateProcessor(
                null!, new RecordingCallbackRouter(), new RecordingCommandDispatcher(), NullLogger<OwnerGatedUpdateProcessor>.Instance));
    }

    [Fact]
    public void A_null_router_is_rejected()
    {
        var authorizer = NewAuthorizer();

        Should.Throw<ArgumentNullException>(() =>
            new OwnerGatedUpdateProcessor(
                authorizer, null!, new RecordingCommandDispatcher(), NullLogger<OwnerGatedUpdateProcessor>.Instance));
    }

    [Fact]
    public void A_null_command_dispatcher_is_rejected()
    {
        var authorizer = NewAuthorizer();

        Should.Throw<ArgumentNullException>(() =>
            new OwnerGatedUpdateProcessor(
                authorizer, new RecordingCallbackRouter(), null!, NullLogger<OwnerGatedUpdateProcessor>.Instance));
    }

    [Fact]
    public void A_null_logger_is_rejected()
    {
        var authorizer = NewAuthorizer();

        Should.Throw<ArgumentNullException>(() =>
            new OwnerGatedUpdateProcessor(
                authorizer, new RecordingCallbackRouter(), new RecordingCommandDispatcher(), null!));
    }

    private static OwnerAuthorizer NewAuthorizer()
    {
        var options = Options.Create(new TelegramOptions { BotToken = "t", AllowedChatIds = [OwnerChat] });
        return new OwnerAuthorizer(options, new CapturingLogger<OwnerAuthorizer>());
    }
}
