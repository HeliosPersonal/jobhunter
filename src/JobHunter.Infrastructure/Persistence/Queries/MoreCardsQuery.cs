using System.Text.Json;
using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Infrastructure.Persistence.Pipeline;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of <c>/more</c> (F10 T06, catalogue §Digest and discovery). Implements
/// <see cref="IMoreCardsQuery"/> with Dapper, read-only (architecture rule 4 forbids a write here): the latest
/// Run's <em>shown but uncarded</em> roles — non-suppressed <c>scores</c> whose job is not one of that Run's
/// digest cards — joined back to <c>jobs</c> and <c>companies</c> for the card's display facts, to the job's
/// most recent <c>enrichments</c> for the fallback <c>(est)</c> salary, and — with a <c>LEFT JOIN LATERAL</c> —
/// to the job's current <c>matches</c> row for the reasons the card explains itself with (invariant 4).
///
/// <para>The "cut" is defined here, not in the handler: a role is below it when it scored high enough to show
/// (not suppressed — a suppression is below the F4 floor, and <c>/hidden</c> owns those) yet ranked outside
/// the digest's top cards (its job id is absent from <c>digest_cards</c> for the latest digest). Only the
/// latest Run's scores are considered — an older Run's below-the-cut set does not linger. The order is the
/// frozen <c>final_score DESC</c> the Run stored, never recomputed, so paging through <c>/more</c> mid-morning
/// keeps the ordering stable ([[PRD]] §8); <c>@Take</c> caps the page while <c>COUNT(*) OVER()</c> reports the
/// full below-the-cut total so the reply can say "Next 5 of 23". It selects <strong>nothing about the Owner's
/// CV</strong> — the CV crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public sealed class MoreCardsQuery(INpgsqlConnectionFactory connectionFactory) : IMoreCardsQuery
{
    private const string Sql =
        """
        WITH latest_run AS (
            SELECT r.id FROM runs r
            ORDER BY r.started_at DESC NULLS LAST
            LIMIT 1
        ),
        carded AS (
            SELECT dc.job_id
            FROM digest_cards dc
            JOIN digests d ON d.id = dc.digest_id
            WHERE d.run_id = (SELECT id FROM latest_run)
        )
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
               e.salary_confidence       AS EstimatedSalaryConfidence,
               COALESCE(
                   (SELECT array_agg(t.technology ORDER BY t.technology)
                    FROM job_technologies t WHERE t.job_id = j.id),
                   ARRAY[]::text[])       AS Highlights,
               s.final_score             AS Score,
               m.reasons                 AS ReasonsJson,
               COUNT(*) OVER()           AS TotalBelowTheCut
        FROM scores s
        JOIN jobs j ON j.id = s.job_id
        JOIN companies c ON c.id = j.company_id
        LEFT JOIN LATERAL (
            SELECT ee.salary_min, ee.salary_max, ee.salary_currency, ee.salary_confidence
            FROM enrichments ee
            WHERE ee.job_id = j.id
            ORDER BY ee.created_at DESC
            LIMIT 1
        ) e ON TRUE
        LEFT JOIN LATERAL (
            SELECT mm.reasons
            FROM matches mm
            WHERE mm.job_id = s.job_id
              AND mm.is_current
            ORDER BY mm.created_at DESC
            LIMIT 1
        ) m ON TRUE
        WHERE NOT s.suppressed
          AND s.run_id = (SELECT id FROM latest_run)
          AND s.job_id NOT IN (SELECT job_id FROM carded)
        ORDER BY s.final_score DESC, s.job_id
        LIMIT @Take
        """;

    private static readonly JsonSerializerOptions LocationJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MoreCardsPage> BelowTheCutAsync(int take, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { Take = take }, cancellationToken: cancellationToken);
        var rows = (await connection.QueryAsync<MoreRow>(command)).ToList();

        var cards = rows
            .Select(r => new MoreCard(
                new CardDisplayFacts(
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
                    r.EstimatedSalaryConfidence,
                    r.Highlights ?? []),
                r.Score,
                StringListJson.Deserialize(r.ReasonsJson)))
            .ToList();

        // COUNT(*) OVER() is evaluated across the whole below-the-cut set before LIMIT, so it is the full
        // total even when @Take caps the page. An empty set returns no rows, so the total is simply zero.
        var total = rows.Count == 0 ? 0 : rows[0].TotalBelowTheCut;

        return new MoreCardsPage(cards, total);
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

    // Init-only properties rather than a positional record: Dapper cannot match a constructor parameter to the
    // text[] Highlights column, so — as CardDisplayQuery does for its technologies array — this row is
    // materialised by property so the array maps cleanly.
    private sealed class MoreRow
    {
        public Guid JobId { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Company { get; init; } = string.Empty;

        public string? Stage { get; init; }

        public string? LocationsJson { get; init; }

        public string RemotePolicy { get; init; } = string.Empty;

        public string ApplyUrl { get; init; } = string.Empty;

        public int? PublishedSalaryMin { get; init; }

        public int? PublishedSalaryMax { get; init; }

        public string? PublishedSalaryCurrency { get; init; }

        public int? EstimatedSalaryMin { get; init; }

        public int? EstimatedSalaryMax { get; init; }

        public string? EstimatedSalaryCurrency { get; init; }

        public decimal? EstimatedSalaryConfidence { get; init; }

        public string[]? Highlights { get; init; }

        public decimal Score { get; init; }

        public string? ReasonsJson { get; init; }

        public int TotalBelowTheCut { get; init; }
    }
}
