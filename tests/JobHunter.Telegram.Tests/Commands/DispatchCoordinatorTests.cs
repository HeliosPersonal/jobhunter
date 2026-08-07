using JobHunter.Application.Commands;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// F10 T03 (SAD §6.1): the dispatch coordinator — the orchestration around the pure
/// <see cref="CommandDispatchPlanner"/> that the four done-when clauses turn on. It applies the per-chat
/// rate limit, gates a state-changing command behind confirmation so there is no path to a handler that
/// bypasses it, and records every terminal invocation with command, outcome, duration and argument
/// <em>count</em> — never argument content. Resolution and parsing are the planner's (unit-tested there);
/// here we assert the ordering and the side effects a chat, a clock and a store make observable.
/// </summary>
public sealed class DispatchCoordinatorTests
{
    private const long OwnerChat = 4242;

    private static CommandDescriptor Ping() =>
        new("ping", "A read command.", [], CommandCapability.Standard, false, "/ping");

    private static CommandDescriptor Run() =>
        new("run", "Start a Run now.", [], CommandCapability.Sensitive, changesState: true, "/run",
            confirmationPrompt: "Start a Run now?");

    private static CommandDescriptor Note() =>
        new("note", "Add a note.",
            [new ArgumentSpec("text", required: true, "The note text.")],
            CommandCapability.Standard, false, "/note");

    private static CommandDescriptor Search() =>
        new("search", "Search live roles.",
            [new ArgumentSpec("query", required: false, "What to search for.")],
            CommandCapability.Standard, false, "/search");

    private sealed class RecordingInvoker
    {
        private readonly IReadOnlyList<RenderedMessage> _reply;
        private readonly Action? _onInvoke;

        public RecordingInvoker(string reply = "ran", Action? onInvoke = null)
        {
            _reply = [RenderedMessage.PlainText(reply)];
            _onInvoke = onInvoke;
        }

        public int Calls { get; private set; }

        public Task<IReadOnlyList<RenderedMessage>> InvokeAsync(
            long chatId, string commandName, string? arguments, CancellationToken cancellationToken)
        {
            Calls++;
            _onInvoke?.Invoke();
            return Task.FromResult(_reply);
        }
    }

    private static DispatchCoordinator Build(
        RecordingInvoker invoker,
        ICommandInvocationLog auditLog,
        IClock clock,
        params CommandDescriptor[] commands) =>
        new(
            new CommandRateLimiter(clock),
            new CommandDispatchPlanner(new CommandRegistry(commands), _ => InlineFilterVocabulary.None),
            auditLog,
            clock,
            new SequentialIdGenerator(),
            invoker.InvokeAsync,
            NullLogger<DispatchCoordinator>.Instance);

    [Fact]
    public async Task A_read_command_reaches_the_handler_and_is_audited_as_succeeded()
    {
        var invoker = new RecordingInvoker("pong");
        var audit = Substitute.For<ICommandInvocationLog>();
        var coordinator = Build(invoker, audit, new FakeClock(), Ping());

        var messages = await coordinator.DispatchAsync(OwnerChat, "/ping", CancellationToken.None);

        invoker.Calls.ShouldBe(1);
        messages.ShouldHaveSingleItem().Text.ShouldBe("pong");
        await audit.Received(1).RecordAsync(
            Arg.Is<CommandInvocation>(i =>
                i!.ChatId == OwnerChat && i.Command == "ping" && i.Outcome == CommandOutcome.Succeeded),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_command_is_never_invoked_and_is_audited_as_unknown()
    {
        var invoker = new RecordingInvoker();
        var audit = Substitute.For<ICommandInvocationLog>();
        var coordinator = Build(invoker, audit, new FakeClock(), Ping());

        var messages = await coordinator.DispatchAsync(OwnerChat, "/teleport now", CancellationToken.None);

        invoker.Calls.ShouldBe(0);
        messages.ShouldHaveSingleItem().Text.ShouldContain("Unknown", Case.Insensitive);
        await audit.Received(1).RecordAsync(
            Arg.Is<CommandInvocation>(i => i!.Outcome == CommandOutcome.Unknown),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_malformed_value_is_never_invoked_and_names_the_problem_with_the_usage_line()
    {
        var invoker = new RecordingInvoker();
        var audit = Substitute.For<ICommandInvocationLog>();
        var coordinator = new DispatchCoordinator(
            new CommandRateLimiter(new FakeClock()),
            new CommandDispatchPlanner(
                new CommandRegistry([Search()]),
                _ => new InlineFilterVocabulary([new InlineFilterSpec("min", InlineFilterKind.Number)])),
            audit,
            new FakeClock(),
            new SequentialIdGenerator(),
            invoker.InvokeAsync,
            NullLogger<DispatchCoordinator>.Instance);

        var messages = await coordinator.DispatchAsync(OwnerChat, "/search min:abc", CancellationToken.None);

        invoker.Calls.ShouldBe(0);
        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("min");
        text.ShouldContain("/search");
        await audit.Received(1).RecordAsync(
            Arg.Is<CommandInvocation>(i => i!.Command == "search" && i.Outcome == CommandOutcome.Malformed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_state_changing_command_is_gated_behind_confirmation_and_never_reaches_the_handler()
    {
        // Done-when #2: no path to a handler bypasses confirmation. The audit is deferred to the tap (T05),
        // because issuing a confirmation is not a terminal outcome.
        var invoker = new RecordingInvoker();
        var audit = Substitute.For<ICommandInvocationLog>();
        var coordinator = Build(invoker, audit, new FakeClock(), Run());

        var messages = await coordinator.DispatchAsync(OwnerChat, "/run", CancellationToken.None);

        invoker.Calls.ShouldBe(0);
        messages.ShouldHaveSingleItem().Text.ShouldContain("Start a Run now", Case.Insensitive);
        await audit.DidNotReceive().RecordAsync(Arg.Any<CommandInvocation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_missing_required_argument_asks_for_it_and_never_reaches_the_handler()
    {
        // The multi-step flow proper is T04; here the guarantee is only that it does not invoke and does not
        // record a terminal outcome for a command that has not run.
        var invoker = new RecordingInvoker();
        var audit = Substitute.For<ICommandInvocationLog>();
        var coordinator = Build(invoker, audit, new FakeClock(), Note());

        var messages = await coordinator.DispatchAsync(OwnerChat, "/note", CancellationToken.None);

        invoker.Calls.ShouldBe(0);
        messages.ShouldHaveSingleItem();
        await audit.DidNotReceive().RecordAsync(Arg.Any<CommandInvocation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_command_after_the_budget_is_throttled_with_exactly_one_message_then_silenced()
    {
        // Done-when #3: the (Budget+1)-th command is one throttle message; every further command in the
        // window is refused silently. Every refused command is still audited as Throttled.
        var invoker = new RecordingInvoker();
        var audit = Substitute.For<ICommandInvocationLog>();
        var coordinator = Build(invoker, audit, new FakeClock(), Ping());

        for (var i = 0; i < CommandRateLimiter.Budget; i++)
        {
            (await coordinator.DispatchAsync(OwnerChat, "/ping", CancellationToken.None)).ShouldHaveSingleItem();
        }

        invoker.Calls.ShouldBe(CommandRateLimiter.Budget);

        var throttled = await coordinator.DispatchAsync(OwnerChat, "/ping", CancellationToken.None);
        throttled.ShouldHaveSingleItem();

        var silenced = await coordinator.DispatchAsync(OwnerChat, "/ping", CancellationToken.None);
        silenced.ShouldBeEmpty();

        invoker.Calls.ShouldBe(CommandRateLimiter.Budget);
        await audit.Received(2).RecordAsync(
            Arg.Is<CommandInvocation>(i => i!.Outcome == CommandOutcome.Throttled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_recorded_duration_reflects_the_clock_across_the_handler_call()
    {
        var clock = new FakeClock();
        var invoker = new RecordingInvoker("pong", onInvoke: () => clock.Advance(TimeSpan.FromMilliseconds(150)));
        var audit = Substitute.For<ICommandInvocationLog>();
        var coordinator = Build(invoker, audit, clock, Ping());

        await coordinator.DispatchAsync(OwnerChat, "/ping", CancellationToken.None);

        await audit.Received(1).RecordAsync(
            Arg.Is<CommandInvocation>(i => i!.DurationMs == 150),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_audit_records_the_argument_count_never_the_argument_content()
    {
        var invoker = new RecordingInvoker();
        var audit = Substitute.For<ICommandInvocationLog>();
        var coordinator = Build(invoker, audit, new FakeClock(), Search());

        await coordinator.DispatchAsync(OwnerChat, "/search kafka remote wroclaw", CancellationToken.None);

        // Three whitespace-separated argument tokens; the invocation type has no field that could hold them.
        await audit.Received(1).RecordAsync(
            Arg.Is<CommandInvocation>(i => i!.Command == "search" && i.ArgCount == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_audit_write_does_not_stop_the_reply_reaching_the_owner()
    {
        // ICommandInvocationLog: recording must never throw in a way that reaches the Owner — a failed audit
        // is an operational fault, not a failed command.
        var invoker = new RecordingInvoker("pong");
        var audit = Substitute.For<ICommandInvocationLog>();
        audit.RecordAsync(Arg.Any<CommandInvocation>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("audit store down")));
        var coordinator = Build(invoker, audit, new FakeClock(), Ping());

        var messages = await coordinator.DispatchAsync(OwnerChat, "/ping", CancellationToken.None);

        messages.ShouldHaveSingleItem().Text.ShouldBe("pong");
        invoker.Calls.ShouldBe(1);
    }

    [Fact]
    public void It_rejects_its_null_collaborators()
    {
        var rateLimiter = new CommandRateLimiter(new FakeClock());
        var planner = new CommandDispatchPlanner(new CommandRegistry([Ping()]), _ => InlineFilterVocabulary.None);
        var audit = Substitute.For<ICommandInvocationLog>();
        var clock = new FakeClock();
        var ids = new SequentialIdGenerator();
        DispatchCoordinator.CommandInvoker invoke = (_, _, _, _) => Task.FromResult<IReadOnlyList<RenderedMessage>>([]);

        Should.Throw<ArgumentNullException>(() => new DispatchCoordinator(
            null!, planner, audit, clock, ids, invoke, NullLogger<DispatchCoordinator>.Instance));
        Should.Throw<ArgumentNullException>(() => new DispatchCoordinator(
            rateLimiter, null!, audit, clock, ids, invoke, NullLogger<DispatchCoordinator>.Instance));
        Should.Throw<ArgumentNullException>(() => new DispatchCoordinator(
            rateLimiter, planner, null!, clock, ids, invoke, NullLogger<DispatchCoordinator>.Instance));
        Should.Throw<ArgumentNullException>(() => new DispatchCoordinator(
            rateLimiter, planner, audit, null!, ids, invoke, NullLogger<DispatchCoordinator>.Instance));
        Should.Throw<ArgumentNullException>(() => new DispatchCoordinator(
            rateLimiter, planner, audit, clock, null!, invoke, NullLogger<DispatchCoordinator>.Instance));
        Should.Throw<ArgumentNullException>(() => new DispatchCoordinator(
            rateLimiter, planner, audit, clock, ids, null!, NullLogger<DispatchCoordinator>.Instance));
    }
}
