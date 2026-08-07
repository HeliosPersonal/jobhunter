using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Commands;

public sealed class CommandInvocationTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 7, 7, 0, 0, TimeSpan.Zero);

    private static CommandInvocation Valid(
        string command = "pipeline",
        CommandOutcome outcome = CommandOutcome.Succeeded,
        int durationMs = 12,
        int argCount = 1) =>
        new(Guid.NewGuid(), chatId: 4242, command, outcome, durationMs, argCount, At);

    [Fact]
    public void Exposes_its_declared_fields()
    {
        var id = Guid.NewGuid();

        var invocation = new CommandInvocation(
            id, chatId: 4242, "search", CommandOutcome.Succeeded, durationMs: 87, argCount: 3, At);

        invocation.Id.ShouldBe(id);
        invocation.ChatId.ShouldBe(4242);
        invocation.Command.ShouldBe("search");
        invocation.Outcome.ShouldBe(CommandOutcome.Succeeded);
        invocation.DurationMs.ShouldBe(87);
        invocation.ArgCount.ShouldBe(3);
        invocation.InvokedAt.ShouldBe(At);
    }

    [Fact]
    public void Rejects_an_empty_id() =>
        Should.Throw<ArgumentException>(() =>
            new CommandInvocation(Guid.Empty, 4242, "search", CommandOutcome.Succeeded, 1, 0, At));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_command(string command) =>
        Should.Throw<ArgumentException>(() => Valid(command: command));

    [Fact]
    public void Rejects_a_null_command() =>
        Should.Throw<ArgumentException>(() => Valid(command: null!));

    [Fact]
    public void Rejects_an_undefined_outcome() =>
        Should.Throw<ArgumentException>(() => Valid(outcome: (CommandOutcome)99));

    [Fact]
    public void Rejects_an_unspecified_outcome()
    {
        // Fail closed, mirroring the descriptor's capability guard: an invocation that forgot its outcome
        // (the default enum value) is a programmer error at construction, not a silent "Succeeded".
        Should.Throw<ArgumentException>(() => Valid(outcome: CommandOutcome.Unspecified));
    }

    [Fact]
    public void Rejects_a_negative_duration() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Valid(durationMs: -1));

    [Fact]
    public void Rejects_a_negative_argument_count() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Valid(argCount: -1));

    [Fact]
    public void Accepts_a_zero_argument_count_and_zero_duration()
    {
        // A no-argument command that resolved instantly is a perfectly ordinary audit row.
        var invocation = Valid(argCount: 0, durationMs: 0);

        invocation.ArgCount.ShouldBe(0);
        invocation.DurationMs.ShouldBe(0);
    }
}
