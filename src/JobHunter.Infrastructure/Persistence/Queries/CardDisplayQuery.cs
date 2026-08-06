using System.Text.Json;
using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of a digest card's <em>display</em> facts (F5 T12). Implements <see cref="ICardDisplayQuery"/>
/// with Dapper, read-only (architecture rule 4 forbids a write here): one row per requested job that exists,
/// joining <c>jobs</c> to <c>companies</c> for the display name and stage and — with a
/// <c>LEFT JOIN LATERAL</c> — to the job's most recent <c>enrichments</c> row for the salary estimate the
/// card falls back to when the board published none (the <c>(est)</c> line). The title, apply URL, published
/// salary and locations come from the job itself.
///
/// <para>Requested in one round-trip via <c>= ANY(@JobIds)</c> so the renderer joins facts for a whole
/// digest at once; a job id with no row is simply absent from the result — the renderer skips it rather than
/// showing a fabricated card. Only the latest estimate is surfaced (the lateral orders by <c>created_at
/// DESC</c>, served by <c>idx_enrichments_job_latest</c>), never a superseded assessment. It selects
/// <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and it is not this one
/// (F4 invariant).</para>
/// </summary>
public sealed class CardDisplayQuery(INpgsqlConnectionFactory connectionFactory) : ICardDisplayQuery
{
    private const string Sql =
        """
        SELECT j.id                      AS JobId,
               j.title                   AS Title,
               c.display_name            AS Company,
               c.stage                   AS Stage,
               j.locations               AS LocationsJson,
               j.remote_policy           AS RemotePolicy,
               j.apply_url               AS ApplyUrl,
               j.salary_min::int         AS PublishedSalaryMin,
               j.salary_max::int         AS PublishedSalaryMax,
               j.salary_currency         AS PublishedSalaryCurrency,
               e.salary_min::int         AS EstimatedSalaryMin,
               e.salary_max::int         AS EstimatedSalaryMax,
               e.salary_currency         AS EstimatedSalaryCurrency,
               e.salary_confidence       AS EstimatedSalaryConfidence
        FROM jobs j
        JOIN companies c ON c.id = j.company_id
        LEFT JOIN LATERAL (
            SELECT ee.salary_min, ee.salary_max, ee.salary_currency, ee.salary_confidence
            FROM enrichments ee
            WHERE ee.job_id = j.id
            ORDER BY ee.created_at DESC
            LIMIT 1
        ) e ON TRUE
        WHERE j.id = ANY(@JobIds)
        """;

    private static readonly JsonSerializerOptions LocationJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<Guid, CardDisplayFacts>> DisplayFactsAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobIds);
        if (jobIds.Count == 0)
        {
            return new Dictionary<Guid, CardDisplayFacts>();
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            Sql, new { JobIds = jobIds.ToArray() }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<DisplayRow>(command);

        return rows
            .Select(r => new CardDisplayFacts(
                r.JobId,
                r.Title,
                r.Company,
                string.IsNullOrWhiteSpace(r.Stage) ? null : r.Stage!.Trim(),
                ParseCountries(r.LocationsJson),
                r.RemotePolicy,
                r.ApplyUrl,
                r.PublishedSalaryMin,
                r.PublishedSalaryMax,
                r.PublishedSalaryCurrency?.Trim(),
                r.EstimatedSalaryMin,
                r.EstimatedSalaryMax,
                r.EstimatedSalaryCurrency?.Trim(),
                r.EstimatedSalaryConfidence))
            .ToDictionary(f => f.JobId);
    }

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

    private sealed record DisplayRow(
        Guid JobId,
        string Title,
        string Company,
        string? Stage,
        string? LocationsJson,
        string RemotePolicy,
        string ApplyUrl,
        int? PublishedSalaryMin,
        int? PublishedSalaryMax,
        string? PublishedSalaryCurrency,
        int? EstimatedSalaryMin,
        int? EstimatedSalaryMax,
        string? EstimatedSalaryCurrency,
        decimal? EstimatedSalaryConfidence);
}
