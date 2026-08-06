using JobHunter.Telegram.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// The placeholder handler for a command whose feature has not shipped yet — <c>/pipeline</c> before F6.
/// It degrades gracefully: a single plain line saying the command is not available yet, so the command is
/// registered (and appears in <c>/help</c>) without pretending to do something it cannot (contract §Commands).
/// </summary>
public sealed class PlaceholderCommandHandlerTests
{
    private const long OwnerChat = 4242;

    [Fact]
    public async Task It_returns_a_single_not_available_line()
    {
        var handler = new PlaceholderCommandHandler("Application tracking");

        var messages = await handler.HandleAsync(new CommandRequest(OwnerChat, null));

        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("Application tracking");
        text.ShouldContain("not available yet", Case.Insensitive);
    }

    [Fact]
    public void A_null_or_blank_feature_name_is_rejected()
    {
        Should.Throw<ArgumentException>(() => new PlaceholderCommandHandler(null!));
        Should.Throw<ArgumentException>(() => new PlaceholderCommandHandler("  "));
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        var handler = new PlaceholderCommandHandler("F6");

        await Should.ThrowAsync<ArgumentNullException>(() => handler.HandleAsync(null!));
    }
}
