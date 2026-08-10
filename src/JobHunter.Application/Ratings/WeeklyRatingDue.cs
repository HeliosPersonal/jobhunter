namespace JobHunter.Application.Ratings;

/// <summary>
/// The weekly tick that asks the Owner to rate the previous week's top-ten delivered cards (F4 T20, D5). It is
/// the empirical counterpart to the golden set: the golden set proves the ranking is <em>stable</em>, this
/// prompt measures whether it is <em>good</em> against the Owner's real judgement. Enqueued by Hangfire and
/// handled by <see cref="WeeklyRatingHandler"/>, which resolves the previous seven-day window, loads its
/// top-ten delivered cards, and sends one "worth opening?" prompt per card — once per week.
///
/// <para>An internal application message, not a cross-boundary integration event, so it lives in the
/// Application layer rather than <c>Contracts</c>. <see cref="DueAt"/> is stamped once when the tick fires and
/// anchors the week under review, so a redelivered tick reproduces the same window and the same round.</para>
/// </summary>
public sealed record WeeklyRatingDue(DateTimeOffset DueAt);
