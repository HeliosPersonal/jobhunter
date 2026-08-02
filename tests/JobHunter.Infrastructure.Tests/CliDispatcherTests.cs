using JobHunter.Worker.Cli;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests;

public sealed class CliDispatcherTests
{
    [Theory]
    [InlineData("migrate", CliCommand.Migrate)]
    [InlineData("MIGRATE", CliCommand.Migrate)]
    [InlineData("replay-dlq", CliCommand.ReplayDlq)]
    public void A_recognised_verb_maps_to_its_command(string verb, CliCommand expected)
    {
        CliDispatcher.TryGetCommand([verb], out var command).ShouldBeTrue();
        command.ShouldBe(expected);
    }

    [Fact]
    public void Leading_flags_are_skipped_when_finding_the_verb()
    {
        CliDispatcher.TryGetCommand(["--verbose", "migrate"], out var command).ShouldBeTrue();
        command.ShouldBe(CliCommand.Migrate);
    }

    [Fact]
    public void No_arguments_means_no_command()
    {
        CliDispatcher.TryGetCommand([], out var command).ShouldBeFalse();
        command.ShouldBeNull();
    }

    [Fact]
    public void An_unknown_verb_means_no_command()
    {
        CliDispatcher.TryGetCommand(["serve"], out var command).ShouldBeFalse();
        command.ShouldBeNull();
    }

    [Fact]
    public void Only_flags_means_no_command()
    {
        CliDispatcher.TryGetCommand(["--list"], out var command).ShouldBeFalse();
        command.ShouldBeNull();
    }

    [Fact]
    public void TryGetCommand_rejects_null_args()
    {
        Should.Throw<ArgumentNullException>(() => CliDispatcher.TryGetCommand(null!, out _));
    }

    [Fact]
    public void The_queue_option_value_is_extracted()
    {
        CliDispatcher.GetQueueOption(["replay-dlq", "--queue", "orders.dlq"]).ShouldBe("orders.dlq");
    }

    [Fact]
    public void The_queue_option_is_null_when_absent()
    {
        CliDispatcher.GetQueueOption(["replay-dlq"]).ShouldBeNull();
    }

    [Fact]
    public void The_queue_option_is_null_when_the_flag_has_no_value()
    {
        CliDispatcher.GetQueueOption(["replay-dlq", "--queue"]).ShouldBeNull();
    }

    [Fact]
    public void The_list_flag_is_detected()
    {
        CliDispatcher.HasListFlag(["replay-dlq", "--list"]).ShouldBeTrue();
        CliDispatcher.HasListFlag(["replay-dlq"]).ShouldBeFalse();
    }
}
