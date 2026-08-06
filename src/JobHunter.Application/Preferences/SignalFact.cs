using JobHunter.Domain.Preferences;

namespace JobHunter.Application.Preferences;

/// <summary>
/// The fitting-time projection of one <see cref="Signal"/> (F7 SAD §5): its id, the reaction
/// <see cref="Kind"/> (which fixes both its polarity and its evidence <see cref="Weight"/>), the
/// <see cref="JobFacts"/> snapshot it reacted to, and when it occurred. It is deliberately lighter than the
/// <see cref="Signal"/> entity — the fitter is a pure function with no repository and no clock, so it
/// consumes a flat value it can be handed by a synthetic generator as easily as by the aggregation query.
///
/// <para>The <see cref="Weight"/> is the signal's <em>stored</em> evidence weight (the <c>numeric(3,1)</c>
/// column), not re-derived from the kind: the SAD §8 table is configuration and a past signal keeps the
/// weight it was captured under.</para>
/// </summary>
public sealed record SignalFact(
    Guid SignalId,
    SignalKind Kind,
    decimal Weight,
    JobFacts Facts,
    DateTimeOffset OccurredAt);
