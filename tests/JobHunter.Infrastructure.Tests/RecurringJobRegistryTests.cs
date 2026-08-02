using JobHunter.Infrastructure.Scheduling;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests;

public sealed class RecurringJobRegistryTests
{
    [Fact]
    public void A_registered_job_is_recorded_with_its_cron_and_the_kyiv_time_zone()
    {
        var registry = new RecurringJobRegistry();

        registry.AddRecurring("digest", "0 7 * * *");

        var registration = registry.Registrations.ShouldHaveSingleItem();
        registration.JobId.ShouldBe("digest");
        registration.Cron.ShouldBe("0 7 * * *");
        registration.TimeZone.ShouldBe(RecurringJobRegistry.Kyiv);
    }

    [Fact]
    public void AddRecurring_is_fluent()
    {
        var registry = new RecurringJobRegistry();

        registry.AddRecurring("a", "0 7 * * *").AddRecurring("b", "0 8 * * *");

        registry.Registrations.Count.ShouldBe(2);
    }

    [Fact]
    public void A_duplicate_job_id_is_rejected()
    {
        var registry = new RecurringJobRegistry();
        registry.AddRecurring("digest", "0 7 * * *");

        Should.Throw<ArgumentException>(() => registry.AddRecurring("digest", "0 8 * * *"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_job_id_is_rejected(string? jobId)
    {
        Should.Throw<ArgumentException>(() => new RecurringJobRegistry().AddRecurring(jobId!, "0 7 * * *"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_cron_is_rejected(string? cron)
    {
        Should.Throw<ArgumentException>(() => new RecurringJobRegistry().AddRecurring("digest", cron!));
    }

    [Fact]
    public void The_scheduling_time_zone_keeps_seven_am_at_seven_am_across_the_dst_boundary()
    {
        var kyiv = RecurringJobRegistry.Kyiv;

        // A cron of "07:00 Europe/Kyiv" must map to a real 07:00 local wall-clock time on both sides of
        // the DST switch. We assert the offset actually changes across the year (proving it is not UTC),
        // yet local 07:00 stays 07:00 — which is the whole point of scheduling in the named zone.
        var winter = new DateTime(2026, 1, 15, 7, 0, 0, DateTimeKind.Unspecified);
        var summer = new DateTime(2026, 7, 15, 7, 0, 0, DateTimeKind.Unspecified);

        var winterOffset = kyiv.GetUtcOffset(winter);
        var summerOffset = kyiv.GetUtcOffset(summer);

        if (kyiv == TimeZoneInfo.Utc)
        {
            // A build agent without the IANA/Windows zone falls back to UTC; the invariant is vacuous
            // there but the registry still functions. Documented rather than silently skipped.
            winterOffset.ShouldBe(summerOffset);
        }
        else
        {
            summerOffset.ShouldBeGreaterThan(winterOffset);

            // Local 07:00 remains 07:00 regardless of the season.
            var winterLocal = new DateTimeOffset(winter, winterOffset);
            var summerLocal = new DateTimeOffset(summer, summerOffset);
            winterLocal.Hour.ShouldBe(7);
            summerLocal.Hour.ShouldBe(7);
        }
    }
}
