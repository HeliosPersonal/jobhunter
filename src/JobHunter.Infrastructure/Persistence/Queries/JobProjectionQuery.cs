using System.Text.Json;
using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Search;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side that assembles the flat <see cref="JobProjectionSource"/> a <see cref="JobDocument"/> is
/// projected from (F9-T02/T08, data-model §Projection). It is the single query the indexing handler, the
/// reconcile and the full rebuild all use, so the document is always derived from the same fields — the
/// whole of QG-1: the index holds nothing not re-derivable from PostgreSQL by this one query. Read-only
/// (Dapper, architecture rule 4); implements the port.
///
/// <para>The enrichment/score/application inputs F3/F4/F6 own are selected as <c>null</c> here — the
/// columns do not exist until those features merge, so the projection is populated with the null the
/// decoupling decision requires rather than a fabricated value. Technologies come from
/// <c>job_technologies</c>; countries are the distinct countries parsed from the <c>locations</c> jsonb.
/// The never-displayed normalised title is deliberately not selected (QG-2).</para>
/// </summary>
public sealed class JobProjectionQuery(INpgsqlConnectionFactory connectionFactory) : IJobProjectionSource
{
    // A job joined to its company, with technologies and location-country arrays aggregated in-query so one
    // row fully describes one document. Only Live jobs are projected: a closed job is deleted from the index.
    private const string SelectClause =
        """
        SELECT j.id                        AS Id,
               j.title                     AS Title,
               j.description               AS Description,
               j.status                    AS Status,
               c.display_name              AS CompanyName,
               c.canonical_domain          AS CompanyDomain,
               c.stage                     AS CompanyStage,
               j.remote_policy             AS RemotePolicy,
               j.seniority                 AS Seniority,
               j.employment_type           AS EmploymentType,
               NULL::text                  AS AiUsage,
               j.salary_min::int           AS SalaryMin,
               j.salary_max::int           AS SalaryMax,
               j.salary_currency           AS SalaryCurrency,
               NULL::double precision      AS Score,
               NULL::text                  AS ApplicationStatus,
               j.posted_at                 AS PostedAt,
               j.first_seen_at             AS FirstSeenAt,
               j.locations                 AS LocationsJson,
               COALESCE(
                   (SELECT array_agg(t.technology ORDER BY t.technology)
                    FROM job_technologies t WHERE t.job_id = j.id),
                   ARRAY[]::text[])         AS Technologies
        FROM jobs j
        JOIN companies c ON c.id = j.company_id
        """;

    private const string ByIdSql = SelectClause + " WHERE j.id = @Id AND j.status = 'Live'";

    private const string LiveSql = SelectClause + " WHERE j.status = 'Live' ORDER BY j.id";

    private static readonly JsonSerializerOptions LocationJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JobProjectionSource?> ProjectAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(ByIdSql, new { Id = jobId }, cancellationToken: cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ProjectionRow>(command);
        return row is null ? null : Map(row);
    }

    public async IAsyncEnumerable<JobProjectionSource> ProjectLiveAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        // Buffered: false streams the live set so a rebuild of a large corpus does not materialise every row
        // in memory at once (AC-10). Ordered by id so a rebuild is deterministic and resumable.
        var command = new CommandDefinition(
            LiveSql, flags: CommandFlags.None, cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<ProjectionRow>(command);
        foreach (var row in rows)
        {
            yield return Map(row);
        }
    }

    private static JobProjectionSource Map(ProjectionRow row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Description = row.Description,
        Status = row.Status,
        CompanyName = row.CompanyName,
        CompanyDomain = row.CompanyDomain,
        CompanyStage = row.CompanyStage,
        Technologies = row.Technologies ?? [],
        Countries = ParseCountries(row.LocationsJson),
        RemotePolicy = row.RemotePolicy,
        Seniority = row.Seniority,
        EmploymentType = row.EmploymentType,
        AiUsage = row.AiUsage,
        SalaryMin = row.SalaryMin,
        SalaryMax = row.SalaryMax,
        SalaryCurrency = row.SalaryCurrency?.Trim(),
        Score = row.Score,
        PostedAt = row.PostedAt,
        FirstSeenAt = row.FirstSeenAt,
        ApplicationStatus = row.ApplicationStatus,
    };

    private static List<string> ParseCountries(string? locationsJson)
    {
        if (string.IsNullOrWhiteSpace(locationsJson))
        {
            return [];
        }

        var rows = JsonSerializer.Deserialize<List<LocationRow>>(locationsJson, LocationJsonOptions) ?? [];
        return rows
            .Select(r => r.Country)
            .Where(country => !string.IsNullOrWhiteSpace(country))
            .Select(country => country!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record LocationRow(string? Country, string? Region, string? City);

    private sealed record ProjectionRow
    {
        public Guid Id { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string CompanyName { get; init; } = string.Empty;

        public string CompanyDomain { get; init; } = string.Empty;

        public string? CompanyStage { get; init; }

        public string RemotePolicy { get; init; } = string.Empty;

        public string? Seniority { get; init; }

        public string EmploymentType { get; init; } = string.Empty;

        public string? AiUsage { get; init; }

        public int? SalaryMin { get; init; }

        public int? SalaryMax { get; init; }

        public string? SalaryCurrency { get; init; }

        public double? Score { get; init; }

        public string? ApplicationStatus { get; init; }

        public DateTimeOffset? PostedAt { get; init; }

        public DateTimeOffset FirstSeenAt { get; init; }

        public string? LocationsJson { get; init; }

        public string[]? Technologies { get; init; }
    }
}
