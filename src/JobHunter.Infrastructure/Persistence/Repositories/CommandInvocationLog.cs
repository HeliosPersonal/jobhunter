using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using Npgsql;
using NpgsqlTypes;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The append-only <c>command_invocations</c> audit (F10 data-model §command_invocations). One plain
/// <c>INSERT</c> per dispatch — there is no <c>ON CONFLICT</c> because an id is minted per attempt and two
/// runs of the same command are two genuine rows, not a duplicate. There is deliberately no update, delete
/// or read path: F10 is a surface, and the usage metric ([[PRD]] §7) reads this table elsewhere. The insert
/// carries only the argument <em>count</em>, never any argument text (SAD §8) — the schema has no column
/// that could hold it.
/// </summary>
internal sealed class CommandInvocationLog(INpgsqlConnectionFactory connectionFactory) : ICommandInvocationLog
{
    private const string RecordSql =
        """
        INSERT INTO command_invocations (id, chat_id, command, outcome, duration_ms, arg_count, invoked_at)
        VALUES (@id, @chat_id, @command, @outcome, @duration_ms, @arg_count, @invoked_at);
        """;

    public async Task RecordAsync(CommandInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(RecordSql, connection);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = invocation.Id });
        command.Parameters.Add(new NpgsqlParameter("chat_id", NpgsqlDbType.Bigint) { Value = invocation.ChatId });
        command.Parameters.Add(new NpgsqlParameter("command", NpgsqlDbType.Text) { Value = invocation.Command });
        command.Parameters.Add(new NpgsqlParameter("outcome", NpgsqlDbType.Text) { Value = invocation.Outcome.ToString() });
        command.Parameters.Add(new NpgsqlParameter("duration_ms", NpgsqlDbType.Integer) { Value = invocation.DurationMs });
        command.Parameters.Add(new NpgsqlParameter("arg_count", NpgsqlDbType.Smallint) { Value = (short)invocation.ArgCount });
        command.Parameters.Add(new NpgsqlParameter("invoked_at", NpgsqlDbType.TimestampTz) { Value = invocation.InvokedAt });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
