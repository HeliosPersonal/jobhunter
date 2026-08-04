using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using Npgsql;
using NpgsqlTypes;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write repository for the re-match backlog (ADR-F4-0002, data-model §cv_versions). Enqueue goes in
/// with <c>ON CONFLICT DO NOTHING</c> against the partial unique index <c>uq_re_match_queue_open</c> — one
/// open request per job — and a <c>RETURNING id</c> present only on a genuine insert, so a single round trip
/// tells a first enqueue from an idempotent no-op with no read-then-write race: re-uploading a CV twice
/// before a Run drains the backlog never queues a job twice. The next Run reads the pending job ids into its
/// matching scope and marks them consumed, so a drained request is not re-matched forever.
/// </summary>
public sealed class ReMatchBacklogRepository(INpgsqlConnectionFactory connectionFactory) : IReMatchBacklog
{
    private const string EnqueueSql =
        """
        INSERT INTO re_match_queue (id, job_id, cv_version_id, tier, enqueued_at, consumed)
        VALUES (@id, @job_id, @cv_version_id, @tier, @enqueued_at, @consumed)
        ON CONFLICT (job_id) WHERE NOT consumed DO NOTHING
        RETURNING id;
        """;

    private const string PendingSql =
        "SELECT job_id FROM re_match_queue WHERE NOT consumed ORDER BY enqueued_at;";

    private const string MarkConsumedSql =
        "UPDATE re_match_queue SET consumed = true WHERE NOT consumed AND job_id = ANY(@job_ids);";

    public async Task<bool> EnqueueAsync(ReMatchQueueItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(EnqueueSql, connection);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = item.Id });
        command.Parameters.Add(new NpgsqlParameter("job_id", NpgsqlDbType.Uuid) { Value = item.JobId });
        command.Parameters.Add(new NpgsqlParameter("cv_version_id", NpgsqlDbType.Uuid) { Value = item.CvVersionId });
        command.Parameters.Add(new NpgsqlParameter("tier", NpgsqlDbType.Text) { Value = item.Tier.ToString() });
        command.Parameters.Add(new NpgsqlParameter("enqueued_at", NpgsqlDbType.TimestampTz) { Value = item.EnqueuedAt });
        command.Parameters.Add(new NpgsqlParameter("consumed", NpgsqlDbType.Boolean) { Value = item.Consumed });

        // DO NOTHING wrote nothing and RETURNING produced no row: an open request for this job already existed.
        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        return insertedId is not null;
    }

    public async Task<IReadOnlyList<Guid>> PendingJobIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(PendingSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var jobIds = new List<Guid>();
        while (await reader.ReadAsync(cancellationToken))
        {
            jobIds.Add(reader.GetGuid(0));
        }

        return jobIds;
    }

    public async Task<int> MarkConsumedAsync(
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobIds);
        if (jobIds.Count == 0)
        {
            return 0;
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(MarkConsumedSql, connection);
        command.Parameters.Add(new NpgsqlParameter("job_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = jobIds.ToArray(),
        });
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
