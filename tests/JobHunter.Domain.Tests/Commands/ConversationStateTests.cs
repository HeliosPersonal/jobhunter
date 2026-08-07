using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Commands;

public sealed class ConversationStateTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 2, 21, 14, 9, TimeSpan.Zero);

    [Fact]
    public void Exposes_the_pending_command_the_awaited_argument_and_when_it_started()
    {
        var state = new ConversationState(
            "note", "text", new Dictionary<string, string> { ["applicationId"] = "0192f8a1" }, Start);

        state.Command.ShouldBe("note");
        state.Awaiting.ShouldBe("text");
        state.Context["applicationId"].ShouldBe("0192f8a1");
        state.StartedAt.ShouldBe(Start);
    }

    [Fact]
    public void Carries_an_empty_context_when_none_is_supplied()
    {
        var state = new ConversationState("note", "text", context: null, Start);

        state.Context.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_command(string command) =>
        Should.Throw<ArgumentException>(() => new ConversationState(command, "text", null, Start));

    [Fact]
    public void Rejects_a_null_command() =>
        Should.Throw<ArgumentException>(() => new ConversationState(null!, "text", null, Start));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_awaited_argument(string awaiting) =>
        Should.Throw<ArgumentException>(() => new ConversationState("note", awaiting, null, Start));

    [Fact]
    public void Reports_whether_it_has_expired_at_a_given_instant()
    {
        var state = new ConversationState("note", "text", null, Start);

        state.HasExpired(Start.AddSeconds(299), ConversationState.Lifetime).ShouldBeFalse();
        state.HasExpired(Start.AddSeconds(301), ConversationState.Lifetime).ShouldBeTrue();
    }

    [Fact]
    public void Expires_exactly_at_its_lifetime_boundary()
    {
        var state = new ConversationState("note", "text", null, Start);

        // The boundary is inclusive: at exactly five minutes the state is gone (data-model: TTL 300s).
        state.HasExpired(Start.AddSeconds(300), ConversationState.Lifetime).ShouldBeTrue();
    }

    [Fact]
    public void Its_lifetime_is_five_minutes() =>
        ConversationState.Lifetime.ShouldBe(TimeSpan.FromMinutes(5));
}
