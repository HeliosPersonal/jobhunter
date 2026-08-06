using JobHunter.Domain.Applications;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind the reminder sweep (F6 SAD §6.2, T06): the non-archived applications whose
/// <c>next_action_at</c> has passed as of <paramref name="now"/> — the ones a reminder may be due for. It is
/// deliberately <b>one indexed query</b> over <c>idx_applications_due</c>, not a scan with per-row logic
/// (done-when 5, SAD §4 S6): <c>next_action_at</c> is a stored column precisely so "what needs attention" is
/// an index range read. Read-only (Dapper, architecture rule 4); defined in Domain so the sweep handler
/// depends on the port, not the SQL.
///
/// <para>Whether a returned application is actually reminded — versus suppressed because the last reminder
/// already fired for the same condition (QG-3) — is the handler's decision, kept out of the query so the read
/// stays a single indexed range. The read carries <strong>nothing about the Owner</strong> (F4 invariant).</para>
/// </summary>
public interface IDueReminderQuery
{
    /// <param name="now">
    /// The sweep instant (from <c>IClock</c>, never <c>DateTime.Now</c>). An application whose
    /// <c>next_action_at</c> is at or before this is due; one scheduled later is excluded by the index range.
    /// </param>
    Task<IReadOnlyList<DueReminder>> DueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
