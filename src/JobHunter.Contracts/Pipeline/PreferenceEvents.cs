namespace JobHunter.Contracts.Pipeline;

/// <summary>
/// The weekly refit produced and activated a new preference model version (event-catalog §3, F7 SAD §6.1).
/// Published by <c>PreferenceLearner</c> inside the same transaction that flips activation, so a consumer
/// reacting to it always finds the newly active model in the store. Consumed by F4's <c>RankingHandler</c>,
/// which picks up the new weights on the next Run — it is not a consequence of any single Owner tap but of a
/// scheduled fit over the whole window, and it feeds the <em>next</em> Run's ranking.
///
/// <para>Idempotency key is the <see cref="Version"/>: a version is monotonic and unique in the store
/// (<c>uq_preference_models_version</c>), so a redelivered activation re-emits the same key and the consumer
/// collapses the duplicate. It carries only the model's identity, version, evidence count and fit instant —
/// nothing about the Owner (F4 invariant).</para>
/// </summary>
public sealed record PreferenceModelUpdated(
    Guid ModelId,
    int Version,
    int SignalCount,
    DateTimeOffset FittedAt,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
