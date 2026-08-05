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

    // ---- T09: the daily digest schedule (02:00 run, 06:45 assembly, 07:00 delivery) -------------

    [Theory]
    [InlineData("daily-run", "0 2 * * *")]
    [InlineData("digest-assembly", "45 6 * * *")]
    [InlineData("digest-delivery", "0 7 * * *")]
    public async Task Each_digest_schedule_binding_is_declared_and_installed_in_the_kyiv_zone(
        string jobId, string cron)
    {
        var registry = new RecurringJobRegistry();
        (string Cron, TimeZoneInfo Zone)? applied = null;
        var binding = new RecurringJobBinding(jobId, cron, (c, z) => applied = (c, z));

        var applier = new RecurringJobApplier(registry, [binding], NullLogger<RecurringJobApplier>.Instance);

        await applier.StartAsync(CancellationToken.None);

        var registration = registry.Registrations.ShouldHaveSingleItem();
        registration.JobId.ShouldBe(jobId);
        registration.Cron.ShouldBe(cron);
        registration.TimeZone.ShouldBe(RecurringJobRegistry.Kyiv);

        applied.ShouldNotBeNull();
        applied!.Value.Cron.ShouldBe(cron);
        // The whole point of declaring the cron in Kyiv (not UTC) is DST stability (QG-1); assert the zone.
        applied.Value.Zone.ShouldBe(RecurringJobRegistry.Kyiv);
    }

    // The 07:00 delivery cron is read in Europe/Kyiv, so the slot is a wall-clock commitment: it stays at
    // 07:00 local across both 2026 DST transitions even though the underlying UTC instant shifts by an hour.
    // Cronos lives inside Hangfire.Core; this asserts the property that guarantee rests on — that 07:00 Kyiv
    // is a stable, unambiguous local time whose UTC offset changes with the season (QG-1, done-when).
    [Theory]
    // The day after spring-forward (29 Mar 2026, +02:00 → +03:00): summer time, 07:00 Kyiv = 04:00 UTC.
    [InlineData(2026, 3, 30, 3, 4)]
    // The day after fall-back (25 Oct 2026, +03:00 → +02:00): winter time, 07:00 Kyiv = 05:00 UTC.
    [InlineData(2026, 10, 26, 2, 5)]
    public void The_seven_am_delivery_slot_is_the_same_wall_clock_across_both_dst_transitions(
        int year, int month, int day, int expectedOffsetHours, int expectedUtcHour)
    {
        var kyiv = RecurringJobRegistry.Kyiv;
        var sevenLocal = new DateTime(year, month, day, 7, 0, 0, DateTimeKind.Unspecified);

        // 07:00 is well clear of the 03:00–04:00 DST gap/overlap, so it is never skipped or ambiguous —
        // which is exactly why the slot is deliverable to the minute every day of the year.
        kyiv.IsInvalidTime(sevenLocal).ShouldBeFalse();
        kyiv.IsAmbiguousTime(sevenLocal).ShouldBeFalse();

        // The local offset flips with the season, proving the zone observes DST rather than sitting at a
        // fixed offset (a fixed-offset zone would drift the wall-clock by an hour for half the year).
        kyiv.GetUtcOffset(sevenLocal).ShouldBe(TimeSpan.FromHours(expectedOffsetHours));

        // Yet the UTC firing instant that keeps the Owner's clock at 07:00 shifts by exactly that hour.
        var utc = TimeZoneInfo.ConvertTimeToUtc(sevenLocal, kyiv);
        utc.Hour.ShouldBe(expectedUtcHour);
        TimeZoneInfo.ConvertTimeFromUtc(utc, kyiv).Hour.ShouldBe(7);
    }
}
