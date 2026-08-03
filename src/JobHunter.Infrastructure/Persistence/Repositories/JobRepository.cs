using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using JobHunter.Infrastructure.Persistence.Jobs;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write repository for the canonical <see cref="Job"/> aggregate (data-model §jobs). The insert path
/// is the concurrency arbiter: the job row goes in with <c>ON CONFLICT (fingerprint) DO NOTHING</c> and a
/// <c>RETURNING id</c> that is present only on a genuine insert, so a single round trip tells an insert
/// from a conflict with no read-then-write race — two consumers dedup'ing one opening produce exactly one
/// job (invariant 2, SAD §6.1). The aliases and technologies are written in the same transaction, so a
/// conflicting insert writes nothing at all. Find and lifecycle mutations go through the EF context, whose
/// tracked aggregate carries its aliases and technologies for T08's closure and reopening.
/// </summary>
public sealed class JobRepository(JobHunterDbContext context, INpgsqlConnectionFactory connectionFactory)
    : IJobRepository
{
    private const string InsertJobSql =
        """
        INSERT INTO jobs
            (id, company_id, origin_raw_posting_id, fingerprint, fingerprint_version, title,
             normalised_title, seniority, description, apply_url, locations, remote_policy,
             employment_type, salary_min, salary_max, salary_currency, salary_period, salary_raw,
             posted_at, posted_at_granularity, first_seen_at, last_seen_at, closed_at, status,
             superseded_by, is_tier2)
        VALUES
            (@id, @company_id, @origin_raw_posting_id, @fingerprint, @fingerprint_version, @title,
             @normalised_title, @seniority, @description, @apply_url, @locations, @remote_policy,
             @employment_type, @salary_min, @salary_max, @salary_currency, @salary_period, @salary_raw,
             @posted_at, @posted_at_granularity, @first_seen_at, @last_seen_at, @closed_at, @status,
             @superseded_by, @is_tier2)
        ON CONFLICT (fingerprint) DO NOTHING
        RETURNING id;
        """;

    private const string InsertAliasSql =
        """
        INSERT INTO job_aliases (job_id, raw_posting_id, source_id, first_seen_at, last_seen_at)
        VALUES (@job_id, @raw_posting_id, @source_id, @first_seen_at, @last_seen_at);
        """;

    private const string InsertTechnologySql =
        """
        INSERT INTO job_technologies (job_id, technology, matched_via)
        VALUES (@job_id, @technology, @matched_via);
        """;

    public async Task<JobInsertOutcome> InsertAsync(Job job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var insertJob = new NpgsqlCommand(InsertJobSql, connection, transaction);
        AddJobParameters(insertJob, job);

        var insertedId = await insertJob.ExecuteScalarAsync(cancellationToken);
        if (insertedId is null)
        {
            // The fingerprint already existed: DO NOTHING wrote nothing and RETURNING produced no row.
            await transaction.RollbackAsync(cancellationToken);
            return JobInsertOutcome.FingerprintConflict;
        }

        foreach (var alias in job.Aliases)
        {
            await using var insertAlias = new NpgsqlCommand(InsertAliasSql, connection, transaction);
            insertAlias.Parameters.Add(new NpgsqlParameter("job_id", NpgsqlDbType.Uuid) { Value = alias.JobId });
            insertAlias.Parameters.Add(new NpgsqlParameter("raw_posting_id", NpgsqlDbType.Uuid) { Value = alias.RawPostingId });
            insertAlias.Parameters.Add(new NpgsqlParameter("source_id", NpgsqlDbType.Uuid) { Value = alias.SourceId });
            insertAlias.Parameters.Add(new NpgsqlParameter("first_seen_at", NpgsqlDbType.TimestampTz) { Value = alias.FirstSeenAt });
            insertAlias.Parameters.Add(new NpgsqlParameter("last_seen_at", NpgsqlDbType.TimestampTz) { Value = alias.LastSeenAt });
            await insertAlias.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var technology in job.Technologies)
        {
            await using var insertTech = new NpgsqlCommand(InsertTechnologySql, connection, transaction);
            insertTech.Parameters.Add(new NpgsqlParameter("job_id", NpgsqlDbType.Uuid) { Value = technology.JobId });
            insertTech.Parameters.Add(new NpgsqlParameter("technology", NpgsqlDbType.Text) { Value = technology.Technology });
            insertTech.Parameters.Add(new NpgsqlParameter("matched_via", NpgsqlDbType.Text) { Value = technology.MatchedVia.ToString() });
            await insertTech.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return JobInsertOutcome.Inserted;
    }

    public Task<Job?> FindByFingerprintAsync(
        Fingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        return TrackedJobs().FirstOrDefaultAsync(j => j.Fingerprint == fingerprint, cancellationToken);
    }

    public Task<Job?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        TrackedJobs().FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    private IQueryable<Job> TrackedJobs() =>
        context.Set<Job>()
            .Include(j => j.Aliases)
            .Include(j => j.Technologies);

    private static void AddJobParameters(NpgsqlCommand command, Job job)
    {
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = job.Id });
        command.Parameters.Add(new NpgsqlParameter("company_id", NpgsqlDbType.Uuid) { Value = job.CompanyId });
        command.Parameters.Add(new NpgsqlParameter("origin_raw_posting_id", NpgsqlDbType.Uuid) { Value = job.OriginRawPostingId });
        command.Parameters.Add(new NpgsqlParameter("fingerprint", NpgsqlDbType.Char) { Value = job.Fingerprint.Value });
        command.Parameters.Add(new NpgsqlParameter("fingerprint_version", NpgsqlDbType.Smallint) { Value = job.FingerprintVersion });
        command.Parameters.Add(new NpgsqlParameter("title", NpgsqlDbType.Text) { Value = job.Title });
        command.Parameters.Add(new NpgsqlParameter("normalised_title", NpgsqlDbType.Text) { Value = job.NormalisedTitle });
        command.Parameters.Add(new NpgsqlParameter("seniority", NpgsqlDbType.Text) { Value = Nullable(job.Seniority?.ToString()) });
        command.Parameters.Add(new NpgsqlParameter("description", NpgsqlDbType.Text) { Value = job.Description });
        command.Parameters.Add(new NpgsqlParameter("apply_url", NpgsqlDbType.Text) { Value = job.ApplyUrl });
        command.Parameters.Add(new NpgsqlParameter("locations", NpgsqlDbType.Jsonb) { Value = LocationSetJson.Serialize(job.Locations) });
        command.Parameters.Add(new NpgsqlParameter("remote_policy", NpgsqlDbType.Text) { Value = job.RemotePolicy.ToString() });
        command.Parameters.Add(new NpgsqlParameter("employment_type", NpgsqlDbType.Text) { Value = job.EmploymentType.ToString() });
        command.Parameters.Add(new NpgsqlParameter("salary_min", NpgsqlDbType.Numeric) { Value = Nullable(job.Salary?.Min) });
        command.Parameters.Add(new NpgsqlParameter("salary_max", NpgsqlDbType.Numeric) { Value = Nullable(job.Salary?.Max) });
        command.Parameters.Add(new NpgsqlParameter("salary_currency", NpgsqlDbType.Char) { Value = Nullable(job.Salary?.Currency) });
        command.Parameters.Add(new NpgsqlParameter("salary_period", NpgsqlDbType.Text) { Value = Nullable(job.Salary?.Period.ToString()) });
        command.Parameters.Add(new NpgsqlParameter("salary_raw", NpgsqlDbType.Text) { Value = Nullable(job.SalaryRaw) });
        command.Parameters.Add(new NpgsqlParameter("posted_at", NpgsqlDbType.TimestampTz) { Value = Nullable(job.PostedAt) });
        command.Parameters.Add(new NpgsqlParameter("posted_at_granularity", NpgsqlDbType.Text) { Value = job.PostedAtGranularity.ToString() });
        command.Parameters.Add(new NpgsqlParameter("first_seen_at", NpgsqlDbType.TimestampTz) { Value = job.FirstSeenAt });
        command.Parameters.Add(new NpgsqlParameter("last_seen_at", NpgsqlDbType.TimestampTz) { Value = job.LastSeenAt });
        command.Parameters.Add(new NpgsqlParameter("closed_at", NpgsqlDbType.TimestampTz) { Value = Nullable(job.ClosedAt) });
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Text) { Value = job.Status.ToString() });
        command.Parameters.Add(new NpgsqlParameter("superseded_by", NpgsqlDbType.Uuid) { Value = Nullable(job.SupersededBy) });
        command.Parameters.Add(new NpgsqlParameter("is_tier2", NpgsqlDbType.Boolean) { Value = job.IsTier2 });
    }

    private static object Nullable<T>(T? value) => value is null ? DBNull.Value : value;
}
