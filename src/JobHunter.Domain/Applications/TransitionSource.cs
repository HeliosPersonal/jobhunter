namespace JobHunter.Domain.Applications;

/// <summary>
/// Where a status change came from (F6 [[data-model]] §application_transitions <c>source</c>). Recorded
/// on every transition so an automatic change (<see cref="System"/>) is distinguishable from a
/// deliberate one months later (SAD §8).
///
/// <para>Persisted as <c>text</c>, never an ordinal (coding-standards §5).</para>
/// </summary>
public enum TransitionSource
{
    /// <summary>A card tap or a command in the Telegram bot.</summary>
    Telegram,

    /// <summary>An operator write through the API.</summary>
    Api,

    /// <summary>An automatic change — a reminder actioned, a sweep — not a deliberate one.</summary>
    System,
}
