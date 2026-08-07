using JobHunter.Application.Commands;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using JobHunter.TestKit;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Commands;

/// <summary>
/// The confirmation gate for state-changing commands (SAD §6.3, AC-07). Issuing stamps a single-use
/// token; redeeming decides — with the clock and whatever the store still holds — whether the tap runs
/// the command, was already used, or expired. The store is faked; the service owns the decision, so the
/// used/expired/mismatch branches are unit-tested with no Redis and no real time.
/// </summary>
public sealed class ConfirmationServiceTests
{
    private const long Chat = 4242;

    private readonly IConfirmationStore _store = Substitute.For<IConfirmationStore>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 8, 2, 21, 14, 9, TimeSpan.Zero));
    private readonly SequentialIdGenerator _ids = new();

    private ConfirmationService NewService() => new(_store, _clock, _ids);

    [Fact]
    public void Rejects_a_null_collaborator()
    {
        Should.Throw<ArgumentNullException>(() => new ConfirmationService(null!, _clock, _ids));
        Should.Throw<ArgumentNullException>(() => new ConfirmationService(_store, null!, _ids));
        Should.Throw<ArgumentNullException>(() => new ConfirmationService(_store, _clock, null!));
    }

    [Fact]
    public async Task Issuing_stamps_a_token_bound_to_the_chat_command_and_arguments_and_stores_it()
    {
        var token = await NewService().IssueAsync(Chat, "run", "2026-08");

        token.ChatId.ShouldBe(Chat);
        token.Command.ShouldBe("run");
        token.ArgumentTail.ShouldBe("2026-08");
        token.IssuedAt.ShouldBe(_clock.UtcNow);
        token.Used.ShouldBeFalse();
        token.Nonce.ShouldNotBeNullOrWhiteSpace();
        await _store.Received(1).IssueAsync(
            Arg.Is<ConfirmationToken>(t => t != null && t.Nonce == token.Nonce && t.Command == "run"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_issued_nonce_fits_well_within_the_sixty_four_byte_callback_cap()
    {
        var token = await NewService().IssueAsync(Chat, "run", string.Empty);

        // The whole callback payload must fit 64 bytes (SAD §2); the nonce alone leaves ample room.
        token.Nonce.Length.ShouldBeLessThanOrEqualTo(32);
    }

    [Fact]
    public async Task A_fresh_unused_token_redeems_as_confirmed_naming_the_command_to_run()
    {
        _store.RedeemAsync("n1", Arg.Any<CancellationToken>())
            .Returns(new ConfirmationToken("n1", Chat, "run", "2026-08", _clock.UtcNow));

        var result = await NewService().RedeemAsync("n1", Chat);

        result.Outcome.ShouldBe(ConfirmationOutcome.Confirmed);
        result.Command.ShouldBe("run");
        result.ArgumentTail.ShouldBe("2026-08");
    }

    [Fact]
    public async Task A_token_the_store_no_longer_holds_redeems_as_expired_so_the_owner_re_issues()
    {
        _store.RedeemAsync("gone", Arg.Any<CancellationToken>()).Returns((ConfirmationToken?)null);

        var result = await NewService().RedeemAsync("gone", Chat);

        result.Outcome.ShouldBe(ConfirmationOutcome.Expired);
    }

    [Fact]
    public async Task A_second_tap_of_an_already_used_token_says_it_was_already_used()
    {
        _store.RedeemAsync("n1", Arg.Any<CancellationToken>())
            .Returns(new ConfirmationToken("n1", Chat, "run", "", _clock.UtcNow, used: true));

        var result = await NewService().RedeemAsync("n1", Chat);

        result.Outcome.ShouldBe(ConfirmationOutcome.AlreadyUsed);
    }

    [Fact]
    public async Task A_token_still_present_but_past_its_lifetime_redeems_as_expired()
    {
        // Belt-and-braces: even if the TTL has not yet swept it, the clock decides it is expired.
        _store.RedeemAsync("n1", Arg.Any<CancellationToken>())
            .Returns(new ConfirmationToken("n1", Chat, "run", "", _clock.UtcNow));
        _clock.Advance(ConfirmationToken.Lifetime + TimeSpan.FromSeconds(1));

        var result = await NewService().RedeemAsync("n1", Chat);

        result.Outcome.ShouldBe(ConfirmationOutcome.Expired);
    }

    [Fact]
    public async Task A_token_tapped_from_a_different_chat_than_it_was_issued_to_is_refused()
    {
        _store.RedeemAsync("n1", Arg.Any<CancellationToken>())
            .Returns(new ConfirmationToken("n1", Chat, "run", "", _clock.UtcNow));

        var result = await NewService().RedeemAsync("n1", chatId: Chat + 1);

        result.Outcome.ShouldBe(ConfirmationOutcome.Mismatch);
    }

    [Fact]
    public async Task At_one_second_before_the_lifetime_the_token_still_confirms()
    {
        _store.RedeemAsync("n1", Arg.Any<CancellationToken>())
            .Returns(new ConfirmationToken("n1", Chat, "run", "", _clock.UtcNow));
        _clock.Advance(ConfirmationToken.Lifetime - TimeSpan.FromSeconds(1));

        var result = await NewService().RedeemAsync("n1", Chat);

        result.Outcome.ShouldBe(ConfirmationOutcome.Confirmed);
    }
}
