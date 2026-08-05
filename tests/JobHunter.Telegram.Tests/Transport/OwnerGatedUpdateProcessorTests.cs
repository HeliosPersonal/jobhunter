using JobHunter.Telegram.Auth;
using JobHunter.Telegram.Tests.Support;
using JobHunter.Telegram.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Transport;

/// <summary>
/// The allowlist gate at the front of update processing (AC-10). An update from an unauthorised chat, or one
/// with no chat at all, is dropped before routing; only the Owner's update passes. Routing itself is T10/T11
/// — here we prove the gate.
/// </summary>
public sealed class OwnerGatedUpdateProcessorTests
{
    private const long OwnerChat = 4242;

    private static OwnerGatedUpdateProcessor Build(out CapturingLogger<OwnerAuthorizer> authLog)
    {
        var options = Options.Create(new TelegramOptions { BotToken = "t", AllowedChatIds = [OwnerChat] });
        authLog = new CapturingLogger<OwnerAuthorizer>();
        var authorizer = new OwnerAuthorizer(options, authLog);
        return new OwnerGatedUpdateProcessor(authorizer, NullLogger<OwnerGatedUpdateProcessor>.Instance);
    }

    private static TelegramUpdate MessageFrom(long chatId, long updateId = 1) =>
        new(updateId, new TelegramMessage(new TelegramChat(chatId), "/digest"), null);

    private static TelegramUpdate CallbackFrom(long chatId, long updateId = 1) =>
        new(updateId, null, new TelegramCallbackQuery("cb1", "ignore:ab12", new TelegramMessage(new TelegramChat(chatId), null)));

    [Fact]
    public async Task An_update_from_an_unauthorised_chat_is_dropped_and_logged()
    {
        var processor = Build(out var authLog);

        await processor.ProcessAsync(MessageFrom(9999));

        authLog.Entries.ShouldContain(e => e.Message.Contains("9999", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_owners_message_passes_the_gate()
    {
        var processor = Build(out var authLog);

        await processor.ProcessAsync(MessageFrom(OwnerChat));

        authLog.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_owners_callback_query_passes_the_gate()
    {
        var processor = Build(out var authLog);

        await processor.ProcessAsync(CallbackFrom(OwnerChat));

        authLog.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_update_with_no_chat_is_dropped()
    {
        var processor = Build(out _);
        var chatless = new TelegramUpdate(1, null, null);

        // No chat means not the Owner's — dropped silently, not routed and not thrown.
        await Should.NotThrowAsync(() => processor.ProcessAsync(chatless));
    }

    [Fact]
    public async Task A_null_update_is_rejected()
    {
        var processor = Build(out _);

        await Should.ThrowAsync<ArgumentNullException>(() => processor.ProcessAsync(null!));
    }

    [Fact]
    public async Task An_authorised_callback_query_without_a_message_chat_is_dropped()
    {
        var processor = Build(out _);
        // A callback with no message (so no chat) resolves to no chat id — treated as not the Owner's.
        var chatless = new TelegramUpdate(1, null, new TelegramCallbackQuery("cb1", "x", null));

        await Should.NotThrowAsync(() => processor.ProcessAsync(chatless));
    }

    [Fact]
    public void A_null_authorizer_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() =>
            new OwnerGatedUpdateProcessor(null!, NullLogger<OwnerGatedUpdateProcessor>.Instance));
    }

    [Fact]
    public void A_null_logger_is_rejected()
    {
        var options = Options.Create(new TelegramOptions { BotToken = "t", AllowedChatIds = [OwnerChat] });
        var authorizer = new OwnerAuthorizer(options, new CapturingLogger<OwnerAuthorizer>());

        Should.Throw<ArgumentNullException>(() => new OwnerGatedUpdateProcessor(authorizer, null!));
    }
}
