using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write repository for the <see cref="Score"/> aggregate (data-model §scores). The upsert goes in
/// with <c>ON CONFLICT (job_id, run_id) DO NOTHING</c> and a <c>RETURNING job_id</c> present only on a
/// genuine insert, so a resumed ranking pass writes each score exactly once. Every component is stored so
/// the total reconciles (QG-1). The aggregate refuses to exist unless the stored total matches its
/// components, so the row that lands is always reconstructable. Reads go through the EF context.
/// </summary>
public sealed class ScoreRepository(JobHunterDbContext context, INpgsqlConnectionFactory connectionFactory)
    : IScoreRepository
{
    private const string UpsertSql =
        """
        INSERT INTO scores
            (job_id, run_id, final_score, match_component, preference_component, freshness_component,
             confidence_multiplier, preference_model_id, suppressed, suppression_reason, computed_at)
        VALUES
            (@job_id, @run_id, @final_score, @match_component, @preference_component, @freshness_component,
             @confidence_multiplier, @preference_model_id, @suppressed, @suppression_reason, @computed_at)
        ON CONFLICT (job_id, run_id) DO NOTHING
        RETURNING job_id;
        """;

    public async Task<bool> UpsertAsync(Score score, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(UpsertSql, connection);
        AddParameters(command, score);

        var insertedKey = await command.ExecuteScalarAsync(cancellationToken);
        // DO NOTHING wrote nothing and RETURNING produced no row: the (job_id, run_id) already existed.
        return insertedKey is not null;
    }

    public Task<Score?> FindAsync(Guid jobId, Guid runId, CancellationToken cancellationToken = default) =>
        context.Set<Score>().FirstOrDefaultAsync(s => s.JobId == jobId && s.RunId == runId, cancellationToken);

    private static void AddParameters(NpgsqlCommand command, Score s)
    {
        command.Parameters.Add(new NpgsqlParameter("job_id", NpgsqlDbType.Uuid) { Value = s.JobId });
        command.Parameters.Add(new NpgsqlParameter("run_id", NpgsqlDbType.Uuid) { Value = s.RunId });
        command.Parameters.Add(new NpgsqlParameter("final_score", NpgsqlDbType.Numeric) { Value = s.FinalScore });
        command.Parameters.Add(new NpgsqlParameter("match_component", NpgsqlDbType.Numeric) { Value = s.Components.Match });
        command.Parameters.Add(new NpgsqlParameter("preference_component", NpgsqlDbType.Numeric) { Value = s.Components.Preference });
        command.Parameters.Add(new NpgsqlParameter("freshness_component", NpgsqlDbType.Numeric) { Value = s.Components.Freshness });
        command.Parameters.Add(new NpgsqlParameter("confidence_multiplier", NpgsqlDbType.Numeric) { Value = s.Components.ConfidenceMultiplier });
        command.Parameters.Add(new NpgsqlParameter("preference_model_id", NpgsqlDbType.Uuid) { Value = Nullable(s.PreferenceModelId) });
        command.Parameters.Add(new NpgsqlParameter("suppressed", NpgsqlDbType.Boolean) { Value = s.Suppressed });
        command.Parameters.Add(new NpgsqlParameter("suppression_reason", NpgsqlDbType.Text) { Value = Nullable(s.SuppressionReason) });
        command.Parameters.Add(new NpgsqlParameter("computed_at", NpgsqlDbType.TimestampTz) { Value = s.ComputedAt });
    }

    private static object Nullable<T>(T? value) => value is null ? DBNull.Value : value;
}
