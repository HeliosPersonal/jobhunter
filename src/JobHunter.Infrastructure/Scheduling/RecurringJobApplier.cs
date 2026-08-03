using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// Closes the gap F0's <see cref="RecurringJobRegistry"/> deliberately left open: the registry collects
/// declarations but nothing installs them in Hangfire. A feature contributes one
/// <see cref="RecurringJobBinding"/> (its id, cron and the body that installs it) from its own
/// composition — no F0 file modified (T10). On start this applier declares each binding through the
/// registry's public <see cref="RecurringJobRegistry.AddRecurring"/> seam and then joins every
/// declaration to its binding to install the schedule, reading the cron and the
/// <see cref="RecurringJobRegistry.Kyiv"/> zone back from the registry so the registry stays the single
/// source of truth for <em>what</em> is scheduled and <em>when</em>.
///
/// A declaration with no matching binding is a composition mismatch surfaced loudly rather than a
/// silently dropped schedule. The <see cref="RecurringJobBinding.Apply"/> body — the one call that
/// reaches Hangfire's static <c>RecurringJob</c> — is exercised by the Worker's integration tests; the
/// join/declare logic here is unit-tested directly with a spy binding.
/// </summary>
internal sealed class RecurringJobApplier(
    RecurringJobRegistry registry,
    IEnumerable<RecurringJobBinding> bindings,
    ILogger<RecurringJobApplier> logger) : IHostedService
{
    private readonly RecurringJobRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly IReadOnlyList<RecurringJobBinding> _bindings =
        (bindings ?? throw new ArgumentNullException(nameof(bindings))).ToList();
    private readonly ILogger<RecurringJobApplier> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Declare each binding into F0's registry (the AC: "registered through F0's RecurringJobRegistry").
        // Guarded so a job already declared elsewhere is not declared twice — AddRecurring rejects a dup id.
        var declared = new HashSet<string>(
            _registry.Registrations.Select(r => r.JobId), StringComparer.Ordinal);

        foreach (var binding in _bindings)
        {
            if (declared.Add(binding.JobId))
            {
                _registry.AddRecurring(binding.JobId, binding.Cron);
            }
        }

        var bindingsById = _bindings.ToDictionary(b => b.JobId, StringComparer.Ordinal);

        foreach (var registration in _registry.Registrations)
        {
            if (!bindingsById.TryGetValue(registration.JobId, out var binding))
            {
                throw new InvalidOperationException(
                    $"Recurring job '{registration.JobId}' is declared but has no job binding to run.");
            }

            binding.Apply(registration.Cron, registration.TimeZone);
            _logger.LogInformation(
                "Applied recurring job {JobId} with cron {Cron}.", registration.JobId, registration.Cron);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
