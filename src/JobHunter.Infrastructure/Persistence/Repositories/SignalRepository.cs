using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using JobHunter.Infrastructure.Persistence.Preferences;
using Npgsql;
using NpgsqlTypes;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// Captures <c>signals</c> (F7 data-model §signals), written by F5 and F6. The insert carries
/// <c>ON CONFLICT (job_id, kind, occurred_at) DO NOTHING</c> with a <c>RETURNING id</c> present only on a
/// genuine insert, so one round trip tells a first capture from a redelivered action with no read-then-write
/// race — the unique constraint arbitrates two racing handlers (idempotence). There is no update or delete
/// path: a signal is a fact about the past, and the fitter reads it, nobody edits it. The <c>job_facts</c>
/// snapshot goes in as <c>jsonb</c> through <see cref="JobFactsJson"/>.
/// </summary>
public sealed class SignalRepository(INpgsqlConnectionFactory connectionFactory) : ISignalRepository
{
    private const string CaptureSql =
        """
        INSERT INTO signals (id, job_id, application_id, kind, weight, job_facts, occurred_at)
        VALUES (@id, @job_id, @application_id, @kind, @weight, @job_facts, @occurred_at)
        ON CONFLICT (job_id, kind, occurred_at) DO NOTHING
        RETURNING id;
        """;

    public async Task<bool> TryCaptureAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(CaptureSql, connection);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = signal.Id });
        command.Parameters.Add(new NpgsqlParameter("job_id", NpgsqlDbType.Uuid) { Value = signal.JobId });
        command.Parameters.Add(new NpgsqlParameter("application_id", NpgsqlDbType.Uuid)
        {
            Value = signal.ApplicationId is null ? DBNull.Value : signal.ApplicationId,
        });
        command.Parameters.Add(new NpgsqlParameter("kind", NpgsqlDbType.Text) { Value = signal.Kind.ToString() });
        command.Parameters.Add(new NpgsqlParameter("weight", NpgsqlDbType.Numeric) { Value = signal.Weight });
        command.Parameters.Add(new NpgsqlParameter("job_facts", NpgsqlDbType.Jsonb)
        {
            Value = JobFactsJson.Serialize(signal.JobFacts),
        });
        command.Parameters.Add(new NpgsqlParameter("occurred_at", NpgsqlDbType.TimestampTz) { Value = signal.OccurredAt });

        // DO NOTHING wrote nothing and RETURNING produced no row: this (job_id, kind, occurred_at) already went.
        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        return insertedId is not null;
    }
}
