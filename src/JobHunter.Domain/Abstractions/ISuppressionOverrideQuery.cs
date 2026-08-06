using JobHunter.Domain.Preferences;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over the Owner's active <see cref="SuppressionOverride"/> rules (F7 data-model
/// §suppression_overrides, AC-06). Ranking consults it once per Run to decide which stated rules outrank the
/// learner: a <see cref="SuppressionMode.NeverSuppress"/> forces a category to keep appearing, a
/// <see cref="SuppressionMode.AlwaysSuppress"/> hides it, whatever the model inferred. Read-only (Dapper);
/// defined in Domain so ranking depends on the port, not the SQL.
///
/// <para>Returns every rule; there are few (one per <c>(dimension, value)</c>, a database constraint) and the
/// caller matches them against each job's facts in memory. On the common day there are none, so the caller can
/// skip fetching per-job facts entirely. It selects <strong>nothing about the Owner's CV</strong> — the CV
/// crosses exactly one boundary, and it is not this one.</para>
/// </summary>
public interface ISuppressionOverrideQuery
{
    /// <summary>The Owner's active override rules, in a stable order; empty when none is set.</summary>
    Task<IReadOnlyList<SuppressionOverride>> AllAsync(CancellationToken cancellationToken = default);
}
