namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// A feature's declaration of a recurring job: its id, its cron, and the body that installs it in Hangfire.
/// A feature contributes one of these from its own composition (T10) — F0's <see cref="RecurringJobRegistry"/>
/// deliberately carries only id/cron/zone and no job body, so it never needs to know what runs. On start
/// <see cref="RecurringJobApplier"/> declares each binding through the registry's public
/// <see cref="RecurringJobRegistry.AddRecurring"/> seam (no F0 file modified) and then calls
/// <see cref="Apply"/> with the cron and the registry's <see cref="RecurringJobRegistry.Kyiv"/> zone, which
/// invokes <c>RecurringJob.AddOrUpdate</c>.
/// </summary>
internal sealed record RecurringJobBinding(string JobId, string Cron, Action<string, TimeZoneInfo> Apply);
