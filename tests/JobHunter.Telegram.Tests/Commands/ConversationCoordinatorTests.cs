using JobHunter.Domain.Commands;
using JobHunter.Telegram.Commands;
using JobHunter.Telegram.Tests.Support;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// The conversation-aware head of dispatch (T10 S5, SAD §6.2). It runs the pure
/// <see cref="Application.Commands.ConversationTurnResolver"/> against whatever is pending for the chat and
/// acts on the disposition: a resume is handed to the pending command through the router; <c>/cancel</c>
/// clears the pending state; a new command supersedes a stale one and routes fresh; and a stray non-command
/// message with nothing pending is treated as an unknown command, never a conversational reply (AC-08, AC-09).
/// </summary>
public sealed class ConversationCoordinatorTests
{
    private const long OwnerChat = 4242;

    private static readonly DateTimeOffset Now = new(2026, 8, 10, 7, 0, 0, TimeSpan.Zero);

    private readonly InMemoryConversationStateStore _state = new();
    private readonly RecordingResumableCommandHandler _note = new(resumeReply: "note-resumed");
    private readonly FakeClock _clock = new(Now);

    private ConversationCoordinator Build()
    {
        var registrations = new[]
        {
            new CommandRegistration("/note", "Attach a note", _note),
        };
        var router = new CommandRouter(registrations, Catalogue, NullLogger<CommandRouter>.Instance);
        return new ConversationCoordinator(_state, router, _clock, NullLogger<ConversationCoordinator>.Instance);
    }

    private static readonly IReadOnlyList<CommandDescriptor> Catalogue =
    [
        new("note", "Attach a note", [],
            CommandCapability.Sensitive, CommandGroup.Pipeline, true, "/note"),
    ];

    [Fact]
    public async Task A_free_text_reply_with_a_pending_command_resumes_that_command()
    {
        _state.Seed(OwnerChat, new ConversationState(
            "note", "text", new Dictionary<string, string> { ["jobId"] = "job-1" }, Now));
        var coordinator = Build();

        var messages = await coordinator.DispatchAsync(OwnerChat, "ping them next week", CancellationToken.None);

        _note.ResumeCalls.ShouldBe(1);
        _note.HandleCalls.ShouldBe(0);
        _note.Resumed.ShouldNotBeNull();
        _note.Resumed!.ChatId.ShouldBe(OwnerChat);
        _note.Resumed.Awaiting.ShouldBe("text");
        _note.Resumed.Context["jobId"].ShouldBe("job-1");
        _note.Resumed.Input.ShouldBe("ping them next week");
        messages.ShouldHaveSingleItem().Text.ShouldBe("note-resumed");
    }

    [Fact]
    public async Task A_free_text_message_with_nothing_pending_is_treated_as_an_unknown_command()
    {
        var coordinator = Build();

        var messages = await coordinator.DispatchAsync(OwnerChat, "just chatting", CancellationToken.None);

        _note.ResumeCalls.ShouldBe(0);
        _note.HandleCalls.ShouldBe(0);
        messages.ShouldHaveSingleItem().Text.ShouldContain("Unknown command", Case.Insensitive);
    }

    [Fact]
    public async Task Cancel_with_a_pending_command_clears_the_state_and_confirms()
    {
        _state.Seed(OwnerChat, new ConversationState("note", "text", null, Now));
        var coordinator = Build();

        var messages = await coordinator.DispatchAsync(OwnerChat, "/cancel", CancellationToken.None);

        _state.Clears.ShouldBe(1);
        _note.ResumeCalls.ShouldBe(0);
        (await _state.GetAsync(OwnerChat)).ShouldBeNull();
        messages.ShouldHaveSingleItem().Text.ShouldContain("Cancelled", Case.Insensitive);
    }

    [Fact]
    public async Task Cancel_with_nothing_pending_says_there_is_nothing_to_cancel()
    {
        var coordinator = Build();

        var messages = await coordinator.DispatchAsync(OwnerChat, "/cancel", CancellationToken.None);

        _note.ResumeCalls.ShouldBe(0);
        messages.ShouldHaveSingleItem().Text.ShouldContain("Nothing to cancel", Case.Insensitive);
    }

    [Fact]
    public async Task A_new_command_supersedes_a_pending_one_clears_it_and_routes_fresh()
    {
        _state.Seed(OwnerChat, new ConversationState("note", "text", null, Now));
        var coordinator = Build();

        var messages = await coordinator.DispatchAsync(OwnerChat, "/note straight away", CancellationToken.None);

        _state.Clears.ShouldBe(1);
        _note.ResumeCalls.ShouldBe(0);
        _note.HandleCalls.ShouldBe(1);
        _note.Handled!.Arguments.ShouldBe("straight away");
        messages.ShouldHaveSingleItem().Text.ShouldBe("handled");
    }

    [Fact]
    public async Task An_expired_pending_command_is_cleared_and_the_message_routes_fresh()
    {
        // The state started a full lifetime ago, so the resolver reports Expired regardless of the message.
        _state.Seed(OwnerChat, new ConversationState("note", "text", null, Now - ConversationState.Lifetime));
        var coordinator = Build();

        var messages = await coordinator.DispatchAsync(OwnerChat, "too late now", CancellationToken.None);

        _state.Clears.ShouldBe(1);
        _note.ResumeCalls.ShouldBe(0);
        _note.HandleCalls.ShouldBe(0);
        // A bare free-text message after expiry is just an unknown command — nothing resumed.
        messages.ShouldHaveSingleItem().Text.ShouldContain("Unknown command", Case.Insensitive);
    }

    [Fact]
    public async Task A_null_message_text_is_rejected()
    {
        var coordinator = Build();

        await Should.ThrowAsync<ArgumentNullException>(() =>
            coordinator.DispatchAsync(OwnerChat, null!, CancellationToken.None));
    }
}
