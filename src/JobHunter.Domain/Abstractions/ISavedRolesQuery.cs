using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over "the roles the Owner has saved" (F5 T11 <c>/saved</c>). A save is a <c>Saved</c>-kind
/// row in the <c>signals</c> table (F7 owns the schema, F5 writes the card action); this port joins those
/// rows back to the job, its company, its latest score and its current match so <c>/saved</c> can render the
/// same card layout the digest uses (AC-12). Read-only (Dapper, architecture rule 4); defined in Domain so
/// the command handler depends on the port, not the SQL.
///
/// <para>Ordered newest-first so the most recently saved role is at the top, and capped at a caller-supplied
/// limit so a long history never produces an unbounded message. It selects <strong>nothing about the
/// Owner</strong> — the CV crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public interface ISavedRolesQuery
{
    /// <summary>The Owner's saved roles, newest-first, capped at <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<SavedRole>> SavedAsync(int limit, CancellationToken cancellationToken = default);
}
