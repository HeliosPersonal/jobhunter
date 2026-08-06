using System.Runtime.CompilerServices;
using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Preferences;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the outcome-signal backfill (F7 T03, done-when 5): the terminal application outcomes that
/// have no captured signal yet, streamed oldest first. It reads <c>application_transitions</c> — the one
/// durable trace of an outcome recorded before F6 began staging signals — joined to <c>applications</c> for
/// the job the outcome is about, filtered to the four outcome statuses, and anti-joined against
/// <c>signals</c> on the exact idempotence key <c>(job_id, kind, occurred_at)</c>. The outcome status name
/// equals the signal kind name (Applied, Interview, Offer, Rejected), so the anti-join compares
/// <c>t.to_status</c> to <c>s.kind</c> directly. The anti-join is what makes a run over a fully migrated
/// history yield nothing — idempotence lives in the query as well as the capture.
///
/// <para>Streamed with <c>QueryUnbufferedAsync</c> so a large history replays with bounded memory. Dapper,
/// read-only (architecture rule 4 forbids a write in the Queries namespace); it selects nothing about the
/// Owner — the CV crosses exactly one boundary, and it is not this one.</para>
/// </summary>
public sealed class BackfillableOutcomeQuery(INpgsqlConnectionFactory connectionFactory) : IBackfillableOutcomeQuery
{
    private const string Sql =
        """
        SELECT a.job_id       AS JobId,
               t.application_id AS ApplicationId,
               t.to_status    AS ToStatus,
               t.occurred_at  AS OccurredAt
        FROM application_transitions t
        JOIN applications a ON a.id = t.application_id
        WHERE t.to_status IN ('Applied', 'Interview', 'Offer', 'Rejected')
          AND t.occurred_at >= @OccurredFrom
          AND NOT EXISTS (
              SELECT 1
              FROM signals s
              WHERE s.job_id = a.job_id
                AND s.kind = t.to_status
                AND s.occurred_at = t.occurred_at
          )
        ORDER BY t.occurred_at
        """;

    public async IAsyncEnumerable<BackfillableOutcome> StreamAsync(
        DateTimeOffset occurredFrom,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        // Unbuffered so a long history is never materialised at once. QueryUnbufferedAsync has only the string
        // overload, which the Dapper.AOT analyser flags with DAP005; we do not opt into AOT interception here.
        // It takes no CancellationToken, so the enumerator's WithCancellation stops the pull between rows.
#pragma warning disable DAP005 // Dapper.AOT interception not enabled — the runtime mapper is intended here.
        var rows = connection.QueryUnbufferedAsync<OutcomeRow>(Sql, new { OccurredFrom = occurredFrom });
#pragma warning restore DAP005
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return new BackfillableOutcome(
                row.JobId,
                row.ApplicationId,
                Enum.Parse<ApplicationStatus>(row.ToStatus),
                row.OccurredAt);
        }
    }

    // Materialised by property so the text to_status maps as a string and is parsed to the enum above, the
    // same tolerant read the application queries use (the status column is text, never an ordinal).
    private sealed class OutcomeRow
    {
        public Guid JobId { get; init; }

        public Guid ApplicationId { get; init; }

        public string ToStatus { get; init; } = string.Empty;

        public DateTimeOffset OccurredAt { get; init; }
    }
}
