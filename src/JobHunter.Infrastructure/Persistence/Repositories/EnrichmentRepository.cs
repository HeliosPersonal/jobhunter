using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Infrastructure.Persistence.Pipeline;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write repository for the <see cref="Enrichment"/> aggregate (data-model §enrichments). The upsert
/// goes in with <c>ON CONFLICT (job_id, run_id) DO NOTHING</c> and a <c>RETURNING id</c> present only on a
/// genuine insert, so a single round trip tells a first write from a replay with no read-then-write race —
/// replaying a half-processed result set writes each enrichment exactly once (AC-06, invariant 3). The
/// aggregate is immutable, so there is deliberately no update path: a correction is a new row for a new
/// Run. Reads go through the EF context.
/// </summary>
public sealed class EnrichmentRepository(JobHunterDbContext context, INpgsqlConnectionFactory connectionFactory)
    : IEnrichmentRepository
{
    private const string UpsertSql =
        """
        INSERT INTO enrichments
            (id, job_id, run_id, salary_min, salary_max, salary_currency, salary_period, salary_confidence,
             is_remote, is_contractor_friendly, timezone_band, ai_usage, company_stage, technologies,
             reasons, prompt_version, created_at)
        VALUES
            (@id, @job_id, @run_id, @salary_min, @salary_max, @salary_currency, @salary_period, @salary_confidence,
             @is_remote, @is_contractor_friendly, @timezone_band, @ai_usage, @company_stage, @technologies,
             @reasons, @prompt_version, @created_at)
        ON CONFLICT (job_id, run_id) DO NOTHING
        RETURNING id;
        """;

    public async Task<bool> UpsertAsync(Enrichment enrichment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enrichment);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(UpsertSql, connection);
        AddParameters(command, enrichment);

        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        // DO NOTHING wrote nothing and RETURNING produced no row: the (job_id, run_id) already existed.
        return insertedId is not null;
    }

    public Task<Enrichment?> FindAsync(Guid jobId, Guid runId, CancellationToken cancellationToken = default) =>
        context.Set<Enrichment>().FirstOrDefaultAsync(e => e.JobId == jobId && e.RunId == runId, cancellationToken);

    private static void AddParameters(NpgsqlCommand command, Enrichment e)
    {
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = e.Id });
        command.Parameters.Add(new NpgsqlParameter("job_id", NpgsqlDbType.Uuid) { Value = e.JobId });
        command.Parameters.Add(new NpgsqlParameter("run_id", NpgsqlDbType.Uuid) { Value = e.RunId });
        command.Parameters.Add(new NpgsqlParameter("salary_min", NpgsqlDbType.Numeric) { Value = Nullable(e.Salary?.Min) });
        command.Parameters.Add(new NpgsqlParameter("salary_max", NpgsqlDbType.Numeric) { Value = Nullable(e.Salary?.Max) });
        command.Parameters.Add(new NpgsqlParameter("salary_currency", NpgsqlDbType.Char) { Value = Nullable(e.Salary?.Currency) });
        command.Parameters.Add(new NpgsqlParameter("salary_period", NpgsqlDbType.Text) { Value = Nullable(e.Salary?.Period.ToString()) });
        command.Parameters.Add(new NpgsqlParameter("salary_confidence", NpgsqlDbType.Numeric) { Value = Nullable(e.Salary?.Confidence) });
        command.Parameters.Add(new NpgsqlParameter("is_remote", NpgsqlDbType.Boolean) { Value = e.IsRemote });
        command.Parameters.Add(new NpgsqlParameter("is_contractor_friendly", NpgsqlDbType.Boolean) { Value = e.IsContractorFriendly });
        command.Parameters.Add(new NpgsqlParameter("timezone_band", NpgsqlDbType.Text) { Value = e.TimezoneBand.ToString() });
        command.Parameters.Add(new NpgsqlParameter("ai_usage", NpgsqlDbType.Text) { Value = e.AiUsage.ToString() });
        command.Parameters.Add(new NpgsqlParameter("company_stage", NpgsqlDbType.Text) { Value = e.CompanyStage.ToString() });
        command.Parameters.Add(new NpgsqlParameter("technologies", NpgsqlDbType.Jsonb) { Value = StringListJson.Serialize(e.Technologies) });
        command.Parameters.Add(new NpgsqlParameter("reasons", NpgsqlDbType.Jsonb) { Value = StringListJson.Serialize(e.Reasons) });
        command.Parameters.Add(new NpgsqlParameter("prompt_version", NpgsqlDbType.Text) { Value = e.PromptVersion });
        command.Parameters.Add(new NpgsqlParameter("created_at", NpgsqlDbType.TimestampTz) { Value = e.CreatedAt });
    }

    private static object Nullable<T>(T? value) => value is null ? DBNull.Value : value;
}
