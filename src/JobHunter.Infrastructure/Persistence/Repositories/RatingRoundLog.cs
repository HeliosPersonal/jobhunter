using JobHunter.Domain.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The append-only <c>rating_round_log</c> (F4 T20 done-when 5, data-model §rating_round_log). A round goes in
/// with <c>ON CONFLICT (week_start, chat_id) DO NOTHING</c> and a <c>RETURNING id</c> present only on a genuine
/// insert, so a single round trip tells the first weekly tick from a redelivery with no read-then-write race —
/// two racing ticks cannot both open the week because the unique constraint arbitrates. There is deliberately
/// no update and no delete: clearing a round would mean re-prompting the Owner for a week already rated, and
/// would double-count the ratings behind <c>precision@10</c>. It mirrors the <see cref="DeliveryLog"/>
/// discipline (invariant 8) but is a separate table so opening a round never looks like a card delivery.
/// </summary>
internal sealed class RatingRoundLog(INpgsqlConnectionFactory connectionFactory, IIdGenerator idGenerator)
    : IRatingRoundLog
{
    private readonly INpgsqlConnectionFactory _connectionFactory =
        connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    private readonly IIdGenerator _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));

    private const string Sql =
        """
        INSERT INTO rating_round_log (id, week_start, chat_id, opened_at)
        VALUES (@id, @week_start, @chat_id, @opened_at)
        ON CONFLICT (week_start, chat_id) DO NOTHING
        RETURNING id;
        """;

    public async Task<bool> TryOpenAsync(
        DateTimeOffset weekStart, long chatId, DateTimeOffset openedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Sql, connection);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = _idGenerator.NewId() });
        command.Parameters.Add(new NpgsqlParameter("week_start", NpgsqlDbType.TimestampTz) { Value = weekStart });
        command.Parameters.Add(new NpgsqlParameter("chat_id", NpgsqlDbType.Bigint) { Value = chatId });
        command.Parameters.Add(new NpgsqlParameter("opened_at", NpgsqlDbType.TimestampTz) { Value = openedAt });

        // DO NOTHING wrote nothing and RETURNING produced no row: this (week_start, chat_id) round already opened.
        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        return insertedId is not null;
    }
}
