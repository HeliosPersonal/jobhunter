using System.Text.Json;
using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Infrastructure.Persistence.Pipeline;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of "the roles the Owner has saved" (F5 T11 <c>/saved</c>). Implements
/// <see cref="ISavedRolesQuery"/> with Dapper, read-only (architecture rule 4 forbids a write here): one row
/// per <c>Saved</c>-kind <c>signals</c> row, joined back to the job for its title, locations and salary, to
/// <c>companies</c> for the display name and stage, to the job's latest <c>scores</c> row for the score the
/// card shows, and — with a <c>LEFT JOIN LATERAL</c> — to the job's current match for the reasons the card
/// explains itself with (invariant 4), so <c>/saved</c> renders the exact same card the digest did (AC-12).
///
/// <para>Only <c>Saved</c> signals are selected: an open or an ignore is a reaction, not a save. Ordered by
/// <c>occurred_at DESC</c> so the most recently saved role is at the top, and capped at the caller's limit so
/// a long history never produces an unbounded message. A superseded match supplies no reasons — the lateral
/// filters on <c>is_current</c> — rather than a stale explanation. It selects <strong>nothing about the
/// Owner</strong> — the CV crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public sealed class SavedRolesQuery(INpgsqlConnectionFactory connectionFactory) : ISavedRolesQuery
{
    private const string Sql =
        """
        SELECT sig.job_id        AS JobId,
               j.title           AS Title,
               c.display_name    AS Company,
               c.stage           AS Stage,
               j.locations       AS LocationsJson,
               j.remote_policy   AS RemotePolicy,
               j.salary_min::int AS SalaryMin,
               j.salary_max::int AS SalaryMax,
               j.salary_currency AS SalaryCurrency,
               COALESCE(sc.final_score, 0) AS Score,
               m.reasons         AS ReasonsJson,
               sig.occurred_at   AS SavedAt
        FROM signals sig
        JOIN jobs j ON j.id = sig.job_id
        JOIN companies c ON c.id = j.company_id
        LEFT JOIN LATERAL (
            SELECT s.final_score
            FROM scores s
            WHERE s.job_id = sig.job_id
            ORDER BY s.computed_at DESC
            LIMIT 1
        ) sc ON TRUE
        LEFT JOIN LATERAL (
            SELECT mm.reasons
            FROM matches mm
            WHERE mm.job_id = sig.job_id
              AND mm.is_current
            ORDER BY mm.created_at DESC
            LIMIT 1
        ) m ON TRUE
        WHERE sig.kind = 'Saved'
        ORDER BY sig.occurred_at DESC
        LIMIT @Limit
        """;

    private static readonly JsonSerializerOptions LocationJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SavedRole>> SavedAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { Limit = limit }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<SavedRow>(command);

        return rows
            .Select(r => new SavedRole(
                r.JobId,
                r.Title,
                r.Company,
                string.IsNullOrWhiteSpace(r.Stage) ? null : r.Stage!.Trim(),
                ParseCountries(r.LocationsJson),
                r.RemotePolicy,
                r.SalaryMin,
                r.SalaryMax,
                r.SalaryCurrency?.Trim(),
                r.Score,
                StringListJson.Deserialize(r.ReasonsJson),
                r.SavedAt))
            .ToList();
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

    private sealed record SavedRow(
        Guid JobId,
        string Title,
        string Company,
        string? Stage,
        string? LocationsJson,
        string RemotePolicy,
        int? SalaryMin,
        int? SalaryMax,
        string? SalaryCurrency,
        decimal Score,
        string? ReasonsJson,
        DateTimeOffset SavedAt);
}
