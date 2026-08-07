using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Commands;

/// <summary>
/// The pure decision at the head of dispatch (SAD §6.2): given whatever conversation is pending, the
/// incoming message and the clock, what should the dispatcher do — resume, tell the Owner it expired,
/// supersede it with a new command, cancel, or just proceed. No store, no clock held, no I/O, so every
/// AC-08 branch is decided here in isolation.
/// </summary>
public sealed class ConversationTurnResolverTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 2, 21, 14, 9, TimeSpan.Zero);

    private static ConversationState Pending(DateTimeOffset? startedAt = null) =>
        new("note", "text", null, startedAt ?? Start);

    [Fact]
    public void With_nothing_pending_a_message_just_proceeds()
    {
        var turn = ConversationTurnResolver.Resolve(pending: null, "/search kafka", Start);

        turn.Disposition.ShouldBe(ConversationDisposition.Proceed);
    }

    [Fact]
    public void A_non_command_message_resumes_a_live_pending_command_with_that_message_as_input()
    {
        var turn = ConversationTurnResolver.Resolve(Pending(), "Sounded promising on the call.", Start.AddSeconds(30));

        turn.Disposition.ShouldBe(ConversationDisposition.Resume);
        turn.Pending!.Command.ShouldBe("note");
        turn.Input.ShouldBe("Sounded promising on the call.");
    }

    [Fact]
    public void A_new_command_supersedes_a_live_pending_one()
    {
        var turn = ConversationTurnResolver.Resolve(Pending(), "/search kafka", Start.AddSeconds(30));

        turn.Disposition.ShouldBe(ConversationDisposition.Superseded);
        turn.Pending!.Command.ShouldBe("note");
    }

    [Fact]
    public void Cancel_with_a_pending_command_cancels_it()
    {
        var turn = ConversationTurnResolver.Resolve(Pending(), "/cancel", Start.AddSeconds(30));

        turn.Disposition.ShouldBe(ConversationDisposition.Cancelled);
        turn.Pending!.Command.ShouldBe("note");
    }

    [Fact]
    public void Cancel_with_nothing_pending_is_a_cheerful_no_op()
    {
        var turn = ConversationTurnResolver.Resolve(pending: null, "/cancel", Start);

        turn.Disposition.ShouldBe(ConversationDisposition.NothingToCancel);
    }

    [Fact]
    public void Cancel_is_honoured_even_when_a_bot_mention_is_appended()
    {
        var turn = ConversationTurnResolver.Resolve(Pending(), "/cancel@JobHunterBot", Start.AddSeconds(30));

        turn.Disposition.ShouldBe(ConversationDisposition.Cancelled);
    }

    [Fact]
    public void At_four_minutes_fifty_nine_the_pending_command_is_still_live_and_resumes()
    {
        var turn = ConversationTurnResolver.Resolve(Pending(), "the note text", Start.AddSeconds(299));

        turn.Disposition.ShouldBe(ConversationDisposition.Resume);
    }

    [Fact]
    public void At_five_minutes_one_the_pending_command_has_expired_and_the_owner_is_told()
    {
        var turn = ConversationTurnResolver.Resolve(Pending(), "the note text", Start.AddSeconds(301));

        turn.Disposition.ShouldBe(ConversationDisposition.Expired);
        turn.Pending!.Command.ShouldBe("note");
    }

    [Fact]
    public void An_empty_message_is_not_a_command_and_resumes_a_live_pending_one()
    {
        // A blank message has no leading slash, so it is input, not a command — it resumes the pending one.
        var turn = ConversationTurnResolver.Resolve(Pending(), "   ", Start.AddSeconds(30));

        turn.Disposition.ShouldBe(ConversationDisposition.Resume);
        turn.Input.ShouldBe("   ");
    }

    [Fact]
    public void An_expired_pending_command_does_not_swallow_a_following_command()
    {
        // Past its lifetime, even a command message is reported expired — the Owner is told, then re-issues.
        var turn = ConversationTurnResolver.Resolve(Pending(), "/search kafka", Start.AddSeconds(301));

        turn.Disposition.ShouldBe(ConversationDisposition.Expired);
    }
}
