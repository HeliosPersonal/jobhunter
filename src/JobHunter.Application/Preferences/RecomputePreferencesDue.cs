namespace JobHunter.Application.Preferences;

/// <summary>
/// The tick that opens a weekly refit (F7 SAD §6.1, T05). Enqueued by Hangfire on the Monday 03:00
/// Europe/Kyiv cron and handled by <see cref="PreferenceLearner"/>, which loads the 180-day signal window,
/// fits it, and — with enough evidence — inserts a new model version and flips activation atomically. It is
/// an internal application message, not a cross-boundary integration event, so it lives in the Application
/// layer rather than in <c>Contracts</c>.
///
/// <para><see cref="FittedAt"/> is the refit instant, stamped once from <c>IClock</c> when the tick fires
/// and reused: it is the recency "now" the fit decays from and the instant a new version is recorded and
/// activated at, so a refit that runs twice for the same instant reads the same window and fits the same
/// model. Weekly, not continuous, so one bad day cannot move the model (SAD §4 S3).</para>
/// </summary>
public sealed record RecomputePreferencesDue(DateTimeOffset FittedAt);
