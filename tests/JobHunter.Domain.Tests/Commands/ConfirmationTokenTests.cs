using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Commands;

public sealed class ConfirmationTokenTests
{
    private static readonly DateTimeOffset Issued = new(2026, 8, 2, 21, 14, 9, TimeSpan.Zero);

    private static ConfirmationToken Token(DateTimeOffset? issuedAt = null, bool used = false) =>
        new("aXf9", chatId: 4242, "run", "2026-08", issuedAt ?? Issued, used);

    [Fact]
    public void Exposes_the_nonce_chat_command_arguments_and_when_it_was_issued()
    {
        var token = Token();

        token.Nonce.ShouldBe("aXf9");
        token.ChatId.ShouldBe(4242);
        token.Command.ShouldBe("run");
        token.ArgumentTail.ShouldBe("2026-08");
        token.IssuedAt.ShouldBe(Issued);
        token.Used.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_nonce(string nonce) =>
        Should.Throw<ArgumentException>(() => new ConfirmationToken(nonce, 1, "run", "", Issued));

    [Fact]
    public void Rejects_a_null_nonce() =>
        Should.Throw<ArgumentException>(() => new ConfirmationToken(null!, 1, "run", "", Issued));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_command(string command) =>
        Should.Throw<ArgumentException>(() => new ConfirmationToken("aXf9", 1, command, "", Issued));

    [Fact]
    public void Rejects_a_null_argument_tail() =>
        Should.Throw<ArgumentNullException>(() => new ConfirmationToken("aXf9", 1, "run", null!, Issued));

    [Fact]
    public void Reports_whether_it_has_expired_at_a_given_instant()
    {
        var token = Token();

        token.HasExpired(Issued.AddSeconds(119), ConfirmationToken.Lifetime).ShouldBeFalse();
        token.HasExpired(Issued.AddSeconds(121), ConfirmationToken.Lifetime).ShouldBeTrue();
    }

    [Fact]
    public void Expires_exactly_at_its_two_minute_boundary()
    {
        var token = Token();

        // The boundary is inclusive: at exactly two minutes the confirmation is gone (data-model: TTL 120s).
        token.HasExpired(Issued.AddSeconds(120), ConfirmationToken.Lifetime).ShouldBeTrue();
    }

    [Fact]
    public void Its_lifetime_is_two_minutes() =>
        ConfirmationToken.Lifetime.ShouldBe(TimeSpan.FromMinutes(2));

    [Fact]
    public void Redeeming_produces_a_used_copy_that_is_otherwise_identical()
    {
        var redeemed = Token().Redeemed();

        redeemed.Used.ShouldBeTrue();
        redeemed.Nonce.ShouldBe("aXf9");
        redeemed.Command.ShouldBe("run");
        redeemed.ArgumentTail.ShouldBe("2026-08");
        redeemed.IssuedAt.ShouldBe(Issued);
    }
}
