using JobHunter.Infrastructure.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests;

/// <summary>
/// T10: the applier closes F0's <see cref="RecurringJobRegistry"/> gap. On start it declares each
/// feature <see cref="RecurringJobBinding"/> through the registry's public <c>AddRecurring</c> seam and
/// then installs it, reading the cron and zone back from the registry so the registry stays the single
/// source of truth. A declaration with no matching binding is surfaced loudly. The install body is
/// replaced by a spy so no Hangfire storage is needed — this is a pure unit test.
/// </summary>
public sealed class RecurringJobApplierTests
{
    [Fact]
    public async Task It_declares_each_binding_into_the_registry_and_installs_it_with_the_registry_cron_and_zone()
    {
        var registry = new RecurringJobRegistry();
        (string Cron, TimeZoneInfo Zone)? applied = null;
        var binding = new RecurringJobBinding(
            "discovery-cycle",
            "0 */6 * * *",
            (cron, zone) => applied = (cron, zone));

        var applier = new RecurringJobApplier(registry, [binding], NullLogger<RecurringJobApplier>.Instance);

        await applier.StartAsync(CancellationToken.None);

        var registration = registry.Registrations.ShouldHaveSingleItem();
        registration.JobId.ShouldBe("discovery-cycle");
        registration.Cron.ShouldBe("0 */6 * * *");

        applied.ShouldNotBeNull();
        applied!.Value.Cron.ShouldBe("0 */6 * * *");
        applied.Value.Zone.ShouldBe(RecurringJobRegistry.Kyiv);
    }

    [Fact]
    public async Task It_does_not_redeclare_a_job_already_present_in_the_registry()
    {
        var registry = new RecurringJobRegistry();
        registry.AddRecurring("discovery-cycle", "0 */6 * * *");

        var installed = 0;
        var binding = new RecurringJobBinding("discovery-cycle", "0 */6 * * *", (_, _) => installed++);
        var applier = new RecurringJobApplier(registry, [binding], NullLogger<RecurringJobApplier>.Instance);

        // AddRecurring throws on a duplicate id; the applier must guard against re-declaring it.
        await Should.NotThrowAsync(() => applier.StartAsync(CancellationToken.None));

        registry.Registrations.Count.ShouldBe(1);
        installed.ShouldBe(1);
    }

    [Fact]
    public async Task A_declaration_without_a_matching_binding_is_surfaced_not_silently_dropped()
    {
        var registry = new RecurringJobRegistry();
        registry.AddRecurring("orphan", "0 7 * * *");

        var applier = new RecurringJobApplier(registry, [], NullLogger<RecurringJobApplier>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(() => applier.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Stop_is_a_no_op()
    {
        var applier = new RecurringJobApplier(
            new RecurringJobRegistry(), [], NullLogger<RecurringJobApplier>.Instance);

        await Should.NotThrowAsync(() => applier.StopAsync(CancellationToken.None));
    }
}
