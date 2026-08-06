namespace JobHunter.Domain.Preferences;

/// <summary>
/// What the Owner did that a <see cref="Signal"/> records (F7 [[data-model]] §signals). The card actions
/// — <see cref="Opened"/>, <see cref="Ignored"/>, <see cref="Saved"/>, <see cref="Rated"/> — are written
/// by F5 when the digest is acted on; the outcome kinds — <see cref="Applied"/>, <see cref="Interview"/>,
/// <see cref="Offer"/>, <see cref="Rejected"/> — are written by F6 as an application progresses. F7 owns
/// the schema and is the only reader.
///
/// <para>Each kind carries a fixed evidence weight (SAD §8), resolved through
/// <see cref="SignalWeights"/>: a card action counts for little, an offer for a great deal, because an
/// outcome the Owner lived through says more about their preference than a glance at a card.</para>
///
/// <para>Persisted as <c>text</c>, never an ordinal (coding-standards §5).</para>
/// </summary>
public enum SignalKind
{
    /// <summary>The Owner opened the card's apply link (F5 card action).</summary>
    Opened,

    /// <summary>The Owner dismissed the card without acting (F5 card action).</summary>
    Ignored,

    /// <summary>The Owner saved the card for later (F5 card action).</summary>
    Saved,

    /// <summary>The Owner rated the card explicitly (F5 card action).</summary>
    Rated,

    /// <summary>The Owner marked the job applied (F6 outcome).</summary>
    Applied,

    /// <summary>The application reached an interview (F6 outcome).</summary>
    Interview,

    /// <summary>The application produced an offer (F6 outcome).</summary>
    Offer,

    /// <summary>The application was rejected (F6 outcome).</summary>
    Rejected,
}
