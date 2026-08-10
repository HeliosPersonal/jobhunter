using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using JobHunter.TestKit;
using JobHunter.Telegram.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// The one residual arm the fixture-driven <see cref="FloorCommandHandlerTests"/> does not reach: the per-character
/// ISO-4217 guard inside <c>IsIsoCurrency</c>. Its existing rejections trip the length check (a currency that is not
/// three characters), so the character loop's reject arm — a three-character code that is the right length but
/// carries a non-A–Z character — is never exercised. That is a forgiving business outcome (the usage line), never an
/// exception, and it previews and stores nothing.
/// </summary>
public sealed class FloorCommandHandlerBranchTests
{
    private const long OwnerChat = 4242;

    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly ISalaryFloorPreviewQuery _preview = Substitute.For<ISalaryFloorPreviewQuery>();
    private readonly IConversationStateStore _state = Substitute.For<IConversationStateStore>();
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly FakeClock _clock = new(Now);

    private FloorCommandHandler NewHandler() => new(
        _preview, _state, _profiles, _clock, NullLogger<FloorCommandHandler>.Instance);

    [Fact]
    public async Task A_three_character_currency_carrying_a_non_letter_is_rejected_without_a_preview_or_a_state()
    {
        // "US1" is the right length but the digit trips the per-character A–Z guard (the loop's reject arm), which
        // the too-long "dollars" rejection in the primary suite short-circuits past at the length check.
        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "120000 US1"));

        messages.ShouldHaveSingleItem().Text.ShouldContain("Usage");
        await _preview.DidNotReceive().CountAffectedAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _state.DidNotReceive().SetAsync(Arg.Any<long>(), Arg.Any<ConversationState>(), Arg.Any<CancellationToken>());
    }
}
