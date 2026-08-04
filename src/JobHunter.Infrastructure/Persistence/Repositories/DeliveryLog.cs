using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The append-only <c>delivery_log</c> (data-model §delivery_log, [[adr/0002-delivery-idempotence|ADR-F5-0002]]).
/// The record goes in with <c>ON CONFLICT (run_id, chat_id, card_key) DO NOTHING</c> and a <c>RETURNING id</c>
/// present only on a genuine insert, so a single round trip tells a first send from a redelivery with no
/// read-then-write race — two racing delivery handlers cannot double-send because the unique constraint
/// arbitrates (invariant 8). There is deliberately no update and no delete: deleting a row would mean
/// re-delivering, the exact failure the log prevents.
/// </summary>
public sealed class DeliveryLog(INpgsqlConnectionFactory connectionFactory) : IDeliveryLog
{
    private const string RecordSql =
        """
        INSERT INTO delivery_log (id, run_id, chat_id, card_key, telegram_message_id, delivered_at)
        VALUES (@id, @run_id, @chat_id, @card_key, @telegram_message_id, @delivered_at)
        ON CONFLICT (run_id, chat_id, card_key) DO NOTHING
        RETURNING id;
        """;

    private const string DeliveredKeysSql =
        "SELECT card_key FROM delivery_log WHERE run_id = @run_id AND chat_id = @chat_id;";

    public async Task<bool> TryRecordAsync(DeliveryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(RecordSql, connection);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = record.Id });
        command.Parameters.Add(new NpgsqlParameter("run_id", NpgsqlDbType.Uuid) { Value = record.RunId });
        command.Parameters.Add(new NpgsqlParameter("chat_id", NpgsqlDbType.Bigint) { Value = record.ChatId });
        command.Parameters.Add(new NpgsqlParameter("card_key", NpgsqlDbType.Text) { Value = record.CardKey.Value });
        command.Parameters.Add(new NpgsqlParameter("telegram_message_id", NpgsqlDbType.Bigint)
        {
            Value = record.TelegramMessageId is null ? DBNull.Value : record.TelegramMessageId,
        });
        command.Parameters.Add(new NpgsqlParameter("delivered_at", NpgsqlDbType.TimestampTz) { Value = record.DeliveredAt });

        // DO NOTHING wrote nothing and RETURNING produced no row: this (run_id, chat_id, card_key) already went.
        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        return insertedId is not null;
    }

    public async Task<IReadOnlyCollection<string>> DeliveredKeysAsync(
        Guid runId,
        long chatId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(DeliveredKeysSql, connection);
        command.Parameters.Add(new NpgsqlParameter("run_id", NpgsqlDbType.Uuid) { Value = runId });
        command.Parameters.Add(new NpgsqlParameter("chat_id", NpgsqlDbType.Bigint) { Value = chatId });

        var keys = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }
}
