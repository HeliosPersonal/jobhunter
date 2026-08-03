using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Infrastructure.Persistence.Pipeline;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write repository for the <see cref="Match"/> aggregate (data-model §matches). The upsert goes in
/// with <c>ON CONFLICT (job_id, run_id, profile_id) DO NOTHING</c> and a <c>RETURNING id</c> present only
/// on a genuine insert, so a single round trip tells a first write from a replay with no read-then-write
/// race — replaying a half-processed result set writes each match exactly once (invariant 3). The
/// aggregate is immutable except for <c>is_current</c>; the re-staling sweep clears that flag in a single
/// UPDATE (AC-08), never deleting. Reads go through the EF context.
/// </summary>
public sealed class MatchRepository(JobHunterDbContext context, INpgsqlConnectionFactory connectionFactory)
    : IMatchRepository
{
    private const string UpsertSql =
        """
        INSERT INTO matches
            (id, job_id, run_id, profile_id, cv_version_id, match_score, interview_probability,
             missing_skills, salary_expectation_min, salary_expectation_max, salary_expectation_currency,
             reasons, is_current, prompt_version, created_at)
        VALUES
            (@id, @job_id, @run_id, @profile_id, @cv_version_id, @match_score, @interview_probability,
             @missing_skills, @salary_expectation_min, @salary_expectation_max, @salary_expectation_currency,
             @reasons, @is_current, @prompt_version, @created_at)
        ON CONFLICT (job_id, run_id, profile_id) DO NOTHING
        RETURNING id;
        """;

    private const string MarkNotCurrentSql =
        "UPDATE matches SET is_current = false WHERE cv_version_id = @cv_version_id AND is_current;";

    public async Task<bool> UpsertAsync(Match match, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(match);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(UpsertSql, connection);
        AddParameters(command, match);

        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        // DO NOTHING wrote nothing and RETURNING produced no row: the (job_id, run_id, profile_id) existed.
        return insertedId is not null;
    }

    public Task<Match?> FindAsync(
        Guid jobId,
        Guid runId,
        Guid profileId,
        CancellationToken cancellationToken = default) =>
        context.Set<Match>()
            .FirstOrDefaultAsync(
                m => m.JobId == jobId && m.RunId == runId && m.ProfileId == profileId,
                cancellationToken);

    public async Task<int> MarkNotCurrentForCvVersionAsync(
        Guid cvVersionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(MarkNotCurrentSql, connection);
        command.Parameters.Add(new NpgsqlParameter("cv_version_id", NpgsqlDbType.Uuid) { Value = cvVersionId });
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(NpgsqlCommand command, Match m)
    {
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = m.Id });
        command.Parameters.Add(new NpgsqlParameter("job_id", NpgsqlDbType.Uuid) { Value = m.JobId });
        command.Parameters.Add(new NpgsqlParameter("run_id", NpgsqlDbType.Uuid) { Value = m.RunId });
        command.Parameters.Add(new NpgsqlParameter("profile_id", NpgsqlDbType.Uuid) { Value = m.ProfileId });
        command.Parameters.Add(new NpgsqlParameter("cv_version_id", NpgsqlDbType.Uuid) { Value = m.CvVersionId });
        command.Parameters.Add(new NpgsqlParameter("match_score", NpgsqlDbType.Smallint) { Value = (short)m.MatchScore });
        command.Parameters.Add(new NpgsqlParameter("interview_probability", NpgsqlDbType.Text) { Value = m.InterviewProbability.ToString() });
        command.Parameters.Add(new NpgsqlParameter("missing_skills", NpgsqlDbType.Jsonb) { Value = StringListJson.Serialize(m.MissingSkills) });
        command.Parameters.Add(new NpgsqlParameter("salary_expectation_min", NpgsqlDbType.Numeric) { Value = Nullable(m.SalaryExpectation?.Min) });
        command.Parameters.Add(new NpgsqlParameter("salary_expectation_max", NpgsqlDbType.Numeric) { Value = Nullable(m.SalaryExpectation?.Max) });
        command.Parameters.Add(new NpgsqlParameter("salary_expectation_currency", NpgsqlDbType.Char) { Value = Nullable(m.SalaryExpectation?.Currency) });
        command.Parameters.Add(new NpgsqlParameter("reasons", NpgsqlDbType.Jsonb) { Value = StringListJson.Serialize(m.Reasons) });
        command.Parameters.Add(new NpgsqlParameter("is_current", NpgsqlDbType.Boolean) { Value = m.IsCurrent });
        command.Parameters.Add(new NpgsqlParameter("prompt_version", NpgsqlDbType.Text) { Value = m.PromptVersion });
        command.Parameters.Add(new NpgsqlParameter("created_at", NpgsqlDbType.TimestampTz) { Value = m.CreatedAt });
    }

    private static object Nullable<T>(T? value) => value is null ? DBNull.Value : value;
}
