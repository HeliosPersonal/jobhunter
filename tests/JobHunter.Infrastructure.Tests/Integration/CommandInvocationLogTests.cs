using JobHunter.Domain.Commands;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F10 T03 (data-model §command_invocations): the append-only audit the dispatcher writes for every
/// command attempt. It owns no foreign key — F10 is a surface — so a row stands alone. The load-bearing
/// property is that the row carries the command, outcome, duration and argument <em>count</em> and has no
/// column that could hold argument content. Requires Docker.
/// </summary>
public sealed class CommandInvocationLogTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 7, 7, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task It_records_an_invocation_with_command_outcome_duration_and_arg_count()
    {
        await using var database = await TestDatabase.CreateAsync();
        var log = new CommandInvocationLog(new NpgsqlConnectionFactory(database.ConnectionString));
        var id = Guid.CreateVersion7();

        await log.RecordAsync(new CommandInvocation(
            id, chatId: 4242, "search", CommandOutcome.Succeeded, durationMs: 87, argCount: 3, At));

        await using var ctx = database.CreateContext();
        var row = await ctx.Set<CommandInvocation>().SingleAsync();
        row.Id.ShouldBe(id);
        row.ChatId.ShouldBe(4242);
        row.Command.ShouldBe("search");
        row.Outcome.ShouldBe(CommandOutcome.Succeeded);
        row.DurationMs.ShouldBe(87);
        row.ArgCount.ShouldBe(3);
        row.InvokedAt.ShouldBe(At);
    }

    [RequiresDockerFact]
    public async Task Every_outcome_persists_as_its_text_name_never_an_ordinal()
    {
        await using var database = await TestDatabase.CreateAsync();
        var log = new CommandInvocationLog(new NpgsqlConnectionFactory(database.ConnectionString));

        await log.RecordAsync(new CommandInvocation(
            Guid.CreateVersion7(), 4242, "forget", CommandOutcome.Unauthorised, 1, 0, At));

        await using var connection = await new NpgsqlConnectionFactory(database.ConnectionString).OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT outcome FROM command_invocations LIMIT 1;", connection);
        var stored = (string?)await command.ExecuteScalarAsync();

        stored.ShouldBe("Unauthorised");
    }

    [RequiresDockerFact]
    public async Task It_appends_rather_than_replaces_so_the_log_grows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var log = new CommandInvocationLog(new NpgsqlConnectionFactory(database.ConnectionString));

        await log.RecordAsync(new CommandInvocation(
            Guid.CreateVersion7(), 4242, "pipeline", CommandOutcome.Succeeded, 10, 0, At));
        await log.RecordAsync(new CommandInvocation(
            Guid.CreateVersion7(), 4242, "pipeline", CommandOutcome.Succeeded, 12, 0, At.AddSeconds(5)));

        await using var ctx = database.CreateContext();
        (await ctx.Set<CommandInvocation>().CountAsync()).ShouldBe(2);
    }
}
