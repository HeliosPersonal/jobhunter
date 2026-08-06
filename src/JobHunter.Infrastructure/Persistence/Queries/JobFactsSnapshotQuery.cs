using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Preferences;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of "the job's facts at the moment of the tap" (F5 T10 AC-08, F7 data-model §signals).
/// Implements <see cref="IJobFactsSnapshotQuery"/> with Dapper, read-only (architecture rule 4 forbids a
/// write here): it projects a live <c>jobs</c> row and its <em>latest</em> <c>enrichments</c> row (by a
/// <c>LEFT JOIN LATERAL</c> ordered on <c>created_at DESC</c>) into the <see cref="JobFacts"/> vocabulary
/// the preference learner keys on. Country comes from the <c>locations</c> jsonb, the deterministic
/// technologies from <c>job_technologies</c>, and company size / timezone band from the latest enrichment
/// — absent when the job was never enriched. The salary band is the one derived value: there is no source
/// column, so <see cref="SalaryBand"/> quantises the published USD annual figure here.
///
/// <para>Only a <see cref="JobStatus.Live"/> job returns facts. A closed, superseded or missing job is
/// <c>null</c>, which the caller turns into the plain "this role has closed" acknowledgement and records
/// nothing invalid (AC-09). It selects <strong>nothing about the Owner</strong> — the CV crosses exactly
/// one boundary, and it is not this one.</para>
/// </summary>
public sealed class JobFactsSnapshotQuery(INpgsqlConnectionFactory connectionFactory) : IJobFactsSnapshotQuery
{
    private const string Sql =
        """
        SELECT j.salary_min      AS SalaryMin,
               j.salary_max      AS SalaryMax,
               j.salary_currency AS SalaryCurrency,
               j.salary_period   AS SalaryPeriod,
               j.remote_policy   AS RemotePolicy,
               j.employment_type AS EmploymentType,
               (
                   SELECT array_agg(DISTINCT elem->>'country')
                   FROM jsonb_array_elements(j.locations) AS elem
               )                 AS Countries,
               (
                   SELECT array_agg(t.technology ORDER BY t.technology)
                   FROM job_technologies t
                   WHERE t.job_id = j.id
               )                 AS Technologies,
               e.company_stage   AS CompanyStage,
               e.timezone_band   AS TimezoneBand
        FROM jobs j
        LEFT JOIN LATERAL (
            SELECT ee.company_stage, ee.timezone_band
            FROM enrichments ee
            WHERE ee.job_id = j.id
            ORDER BY ee.created_at DESC
            LIMIT 1
        ) e ON TRUE
        WHERE j.id = @JobId
          AND j.status = 'Live'
        """;

    public async Task<JobFacts?> SnapshotAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { JobId = jobId }, cancellationToken: cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SnapshotRow>(command);
        if (row is null)
        {
            // The job is gone, closed or superseded — absence is a value, not an exception (AC-09).
            return null;
        }

        var facts = new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.RemotePolicy] = [row.RemotePolicy],
            [Dimension.EmploymentType] = [row.EmploymentType],
        };

        var band = SalaryBand.Of(BuildSalary(row));
        if (band is not null)
        {
            facts[Dimension.SalaryBand] = [band];
        }

        if (row.Countries is { Length: > 0 })
        {
            facts[Dimension.Country] = row.Countries;
        }

        if (row.Technologies is { Length: > 0 })
        {
            facts[Dimension.Technology] = row.Technologies;
        }

        if (row.CompanyStage is not null)
        {
            facts[Dimension.CompanySize] = [row.CompanyStage];
        }

        if (row.TimezoneBand is not null)
        {
            facts[Dimension.TimezoneBand] = [row.TimezoneBand];
        }

        // JobFacts.Create trims, de-dups and drops blanks; a live job always keeps remote policy and
        // employment type, so the snapshot is never factless.
        return JobFacts.Create(facts);
    }

    private static SalaryRange? BuildSalary(SnapshotRow row)
    {
        if (row.SalaryMin is null && row.SalaryMax is null)
        {
            return null;
        }

        if (!Enum.TryParse<SalaryPeriod>(row.SalaryPeriod, out var period))
        {
            return null;
        }

        var built = SalaryRange.TryCreate(row.SalaryMin, row.SalaryMax, row.SalaryCurrency, period);
        return built.IsSuccess ? built.Value : null;
    }

    // Init-only properties rather than a positional record: Dapper cannot match a constructor parameter to the
    // text[] Countries/Technologies columns, so — as CardDisplayQuery and JobProjectionQuery do for their array
    // columns — this row is materialised by property so the arrays map cleanly.
    private sealed class SnapshotRow
    {
        public decimal? SalaryMin { get; init; }

        public decimal? SalaryMax { get; init; }

        public string? SalaryCurrency { get; init; }

        public string? SalaryPeriod { get; init; }

        public string RemotePolicy { get; init; } = string.Empty;

        public string EmploymentType { get; init; } = string.Empty;

        public string[]? Countries { get; init; }

        public string[]? Technologies { get; init; }

        public string? CompanyStage { get; init; }

        public string? TimezoneBand { get; init; }
    }
}
