using JobHunter.Application.Applications;
using JobHunter.Domain.Applications;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Applications;

/// <summary>
/// T06 done-when 4: the reminder thresholds are configuration, not hard-coded durations. <see cref="ReminderOptions"/>
/// binds day counts per status and builds the <see cref="ReminderPolicy"/> the sweep resolves through — so a
/// threshold change takes effect on the next sweep with no per-application rescheduling. Its defaults are the
/// SAD §8 durations, so an unconfigured deployment behaves exactly as before.
/// </summary>
public sealed class ReminderOptionsTests
{
    [Fact]
    public void The_defaults_are_the_sad_thresholds()
    {
        var policy = new ReminderOptions().ToPolicy();

        policy.ThresholdFor(ApplicationStatus.Applied).ShouldBe(TimeSpan.FromDays(10));
        policy.ThresholdFor(ApplicationStatus.Interview).ShouldBe(TimeSpan.FromDays(7));
        policy.ThresholdFor(ApplicationStatus.Saved).ShouldBe(TimeSpan.FromDays(5));
    }

    [Fact]
    public void A_status_with_no_configured_threshold_has_nothing_to_chase()
    {
        var policy = new ReminderOptions().ToPolicy();

        policy.ThresholdFor(ApplicationStatus.New).ShouldBeNull();
        policy.ThresholdFor(ApplicationStatus.Rejected).ShouldBeNull();
    }

    [Fact]
    public void Configured_day_counts_override_the_defaults()
    {
        var options = new ReminderOptions { AppliedDays = 14, InterviewDays = 3, SavedDays = 2 };

        var policy = options.ToPolicy();

        policy.ThresholdFor(ApplicationStatus.Applied).ShouldBe(TimeSpan.FromDays(14));
        policy.ThresholdFor(ApplicationStatus.Interview).ShouldBe(TimeSpan.FromDays(3));
        policy.ThresholdFor(ApplicationStatus.Saved).ShouldBe(TimeSpan.FromDays(2));
    }
}
