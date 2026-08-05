using JobHunter.Telegram.Auth;
using JobHunter.Telegram.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Auth;

/// <summary>
/// The allowlist that fronts every update (ADR-0014, AC-10). The load-bearing rules: an allowlisted chat is
/// the Owner; any other chat is rejected and its id logged at warning level; and the check is a pure set
/// membership test — no network, no handler.
/// </summary>
public sealed class OwnerAuthorizerTests
{
    private const long OwnerChat = 4242;

    private static (OwnerAuthorizer Authorizer, CapturingLogger<OwnerAuthorizer> Logger) Build(params long[] allowed)
    {
        var options = Options.Create(new TelegramOptions { BotToken = "t", AllowedChatIds = allowed });
        var logger = new CapturingLogger<OwnerAuthorizer>();
        return (new OwnerAuthorizer(options, logger), logger);
    }

    [Fact]
    public void An_allowlisted_chat_is_the_owner()
    {
        var (authorizer, logger) = Build(OwnerChat);

        authorizer.IsOwner(OwnerChat).ShouldBeTrue();
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void An_unlisted_chat_is_rejected()
    {
        var (authorizer, _) = Build(OwnerChat);

        authorizer.IsOwner(9999).ShouldBeFalse();
    }

    [Fact]
    public void A_rejected_chat_is_logged_at_warning_with_its_id()
    {
        var (authorizer, logger) = Build(OwnerChat);

        authorizer.IsOwner(9999);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("9999");
    }

    [Fact]
    public void An_allowlist_may_hold_more_than_one_chat()
    {
        var (authorizer, _) = Build(1, 2, 3);

        authorizer.IsOwner(2).ShouldBeTrue();
        authorizer.IsOwner(4).ShouldBeFalse();
    }

    [Fact]
    public void A_null_options_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() =>
            new OwnerAuthorizer(null!, new CapturingLogger<OwnerAuthorizer>()));
    }

    [Fact]
    public void A_null_logger_is_rejected()
    {
        var options = Options.Create(new TelegramOptions { BotToken = "t", AllowedChatIds = [OwnerChat] });

        Should.Throw<ArgumentNullException>(() => new OwnerAuthorizer(options, null!));
    }
}
