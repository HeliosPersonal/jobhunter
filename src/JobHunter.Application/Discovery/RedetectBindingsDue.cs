namespace JobHunter.Application.Discovery;

/// <summary>
/// The daily tick that opens a binding re-detection run (SAD §6.2, AC-05). Enqueued by Hangfire and
/// handled by <see cref="RedetectBindingHandler"/>, which probes only the companies due today — those with
/// a stale binding or two consecutive empty cycles, and only the day's bucket so re-detection is spread
/// across the week rather than stampeding on Monday. It is an internal application message, not a
/// cross-boundary integration event, so it lives in the Application layer rather than in <c>Contracts</c>.
/// <see cref="WindowStart"/> is the tick's instant; the day bucket is derived from it.
/// </summary>
public sealed record RedetectBindingsDue(DateTimeOffset WindowStart);
