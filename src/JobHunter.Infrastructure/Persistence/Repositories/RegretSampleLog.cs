using JobHunter.Domain.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The append-only <c>regret_sample_log</c> (F4 T21, ADR-F4-0003). A sample goes in with
/// <c>ON CONFLICT (week_start) DO NOTHING</c> and a <c>RETURNING id</c> present only on a genuine insert, so a
/// single round trip tells the first weekly tick from a redelivery with no read-then-write race — two racing
/// ticks cannot both open the week because the unique constraint arbitrates. There is deliberately no update and
/// no delete: clearing a sample would mean re-running the cheap-tier match for a week already sampled, and would
/// double-spend. It mirrors the <see cref="RatingRoundLog"/> discipline but is keyed by <c>week_start</c> alone,
/// because the sample serves the single Owner rather than a chat.
/// </summary>
internal sealed class RegretSampleLog(INpgsqlConnectionFactory connectionFactory, IIdGenerator idGenerator)
    : IRegretSampleLog
{
    private readonly INpgsqlConnectionFactory _connectionFactory =
        connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    private readonly IIdGenerator _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));

    private const string Sql =
        """
        INSERT INTO regret_sample_log (id, week_start, opened_at)
        VALUES (@id, @week_start, @opened_at)
        ON CONFLICT (week_start) DO NOTHING
        RETURNING id;
        """;

    public async Task<bool> TryOpenAsync(
        DateTimeOffset weekStart, DateTimeOffset openedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Sql, connection);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = _idGenerator.NewId() });
        command.Parameters.Add(new NpgsqlParameter("week_start", NpgsqlDbType.TimestampTz) { Value = weekStart });
        command.Parameters.Add(new NpgsqlParameter("opened_at", NpgsqlDbType.TimestampTz) { Value = openedAt });

        // DO NOTHING wrote nothing and RETURNING produced no row: this week's sample already opened.
        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        return insertedId is not null;
    }
}
