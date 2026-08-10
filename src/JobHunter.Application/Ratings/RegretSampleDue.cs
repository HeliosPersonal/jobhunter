namespace JobHunter.Application.Ratings;

/// <summary>
/// The weekly tick that runs the pre-match filter's falsification control (F4 T21, ADR-F4-0003). It is the
/// adversarial counterpart to <see cref="WeeklyRatingDue"/>: where the rating loop asks whether the jobs the
/// Owner <em>was</em> shown were worth showing, this asks whether a job the filter <em>hid</em> should have
/// been shown. Enqueued by Hangfire and handled by <see cref="RegretSampler"/>, which opens the week's sample
/// once, matches the excluded sample at the cheap tier, records the regret gauge and alerts on any regret.
///
/// <para>An internal application message, not a cross-boundary integration event, so it lives in the
/// Application layer rather than <c>Contracts</c>. <see cref="DueAt"/> is stamped once when the tick fires and
/// anchors the week under review, so a redelivered tick reproduces the same window and the same sample.</para>
/// </summary>
public sealed record RegretSampleDue(DateTimeOffset DueAt);
