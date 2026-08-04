namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over the <em>active</em> learned preference model (F4 SAD §6.2; the model itself is fitted by
/// F7, which F4 only consumes — epic §Scope). Given the jobs a Run is ranking, it returns the model's
/// per-job preference component in <c>[0,1]</c> together with the model's id, or <c>null</c> when no model is
/// active yet. F4 ships before F7, so the default implementation returns <c>null</c> and ranking renormalises
/// the preference weight away; when F7 lands it supplies a real implementation without any change to ranking.
///
/// <para>Keeping preference a <em>separate</em> read from matching means a preference refit never risks a model
/// regression and never costs a deep-tier re-run (ADR-F4-0001): re-ranking with a new model is free. The id is
/// stamped on every score so a bad refit is attributable after the fact (AC-04).</para>
/// </summary>
public interface IPreferenceModelQuery
{
    /// <summary>
    /// The active preference model's contribution for <paramref name="jobIds"/>, or <c>null</c> when none is
    /// active. When present, <see cref="ActivePreference.ComponentByJob"/> holds a value in <c>[0,1]</c> for
    /// each job the model has an opinion on; a job absent from the map has no learned preference and is scored
    /// with the preference weight renormalised away.
    /// </summary>
    Task<ActivePreference?> FindActiveAsync(
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The active preference model's ranking contribution (F4 SAD §6.2). <paramref name="ModelId"/> is stamped on
/// every score it influenced so a refit is attributable (AC-04); <paramref name="ComponentByJob"/> maps a job
/// to its preference component in <c>[0,1]</c>, and a job it omits simply has no learned preference.
/// </summary>
public sealed record ActivePreference(Guid ModelId, IReadOnlyDictionary<Guid, decimal> ComponentByJob);
