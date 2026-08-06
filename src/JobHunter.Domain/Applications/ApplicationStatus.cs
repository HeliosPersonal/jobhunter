namespace JobHunter.Domain.Applications;

/// <summary>
/// The lifecycle state of an <see cref="Application"/> (F6 [[data-model]] §applications <c>status</c>).
/// Seven states, related by a permissive transition table rather than a rigid state machine
/// ([[adr/0001-permissive-transitions-with-history|ADR-F6-0001]]): real hiring does not respect a
/// diagram, so only genuinely impossible sequences are refused.
///
/// <para>Persisted as <c>text</c>, never an ordinal (coding-standards §5).</para>
/// </summary>
public enum ApplicationStatus
{
    /// <summary>The lazily-created entry state: a card was acted on but no stage was chosen yet. There is no
    /// legitimate move back to "not yet acted on", so <c>X → New</c> is refused for every source but itself.</summary>
    New,

    /// <summary>Kept for later, not yet applied to.</summary>
    Saved,

    /// <summary>The Owner has applied. <c>applied_at</c> is set once on first entry and never changed.</summary>
    Applied,

    /// <summary>At least one interview round reached; a further <c>Interview → Interview</c> records a second round.</summary>
    Interview,

    /// <summary>Rejected, or an offer declined. A role can re-open (<c>Rejected → Applied</c>).</summary>
    Rejected,

    /// <summary>An offer was made. Accepted or declined (<c>Offer → Rejected</c>) — never ignored.</summary>
    Offer,

    /// <summary>Dismissed. A status alongside the pipeline, not a deletion — an ignored job is preference
    /// evidence F7 needs.</summary>
    Ignored,
}
