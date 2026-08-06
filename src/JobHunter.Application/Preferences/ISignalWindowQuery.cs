namespace JobHunter.Application.Preferences;

/// <summary>
/// Reads the signals the weekly refit fits on — every <see cref="SignalFact"/> whose reaction occurred on or
/// after a cutoff, newest first (F7 SAD §6.1). It is the read side that turns the append-only <c>signals</c>
/// table into the fitter's input, projecting each row's snapshotted <c>job_facts</c> jsonb straight into the
/// <see cref="Domain.Preferences.JobFacts"/> vocabulary <see cref="WeightFitter"/> keys on — never a join to
/// <c>jobs</c>, so a later edit cannot rewrite what the Owner reacted to.
///
/// <para>Defined here rather than in <c>Domain.Abstractions</c> because <see cref="SignalFact"/> is an
/// Application projection, not a domain type; the Infrastructure implementation depends inward on this port,
/// which the architecture arrow allows. Read-only: it never writes (architecture rule 4).</para>
/// </summary>
public interface ISignalWindowQuery
{
    /// <summary>
    /// The signals with <c>occurred_at &gt;= <paramref name="occurredFrom"/></c>, materialised as
    /// <see cref="SignalFact"/>s. The caller (the learner) passes <c>referenceTime − window</c> as the cutoff,
    /// so the query need not know the window; it simply returns everything at or after the given instant.
    /// </summary>
    Task<IReadOnlyList<SignalFact>> LoadSince(
        DateTimeOffset occurredFrom, CancellationToken cancellationToken = default);
}
