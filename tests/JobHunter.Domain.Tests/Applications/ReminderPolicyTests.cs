using JobHunter.Domain.Applications;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Applications;

/// <summary>
/// T01: reminder thresholds are configuration, not hard-coded durations. The policy maps a status to the
/// time it may sit before a reminder is due (SAD §8: Applied 10 d, Interview 7 d, Saved 5 d); the statuses
/// with nothing to chase have no threshold.
/// </summary>
public sealed class ReminderPolicyTests
{
    [Fact]
    public void The_default_matches_the_SAD_table()
    {
        var policy = ReminderPolicy.Default;

        policy.ThresholdFor(ApplicationStatus.Applied).ShouldBe(TimeSpan.FromDays(10));
        policy.ThresholdFor(ApplicationStatus.Interview).ShouldBe(TimeSpan.FromDays(7));
        policy.ThresholdFor(ApplicationStatus.Saved).ShouldBe(TimeSpan.FromDays(5));
    }

    [Theory]
    [InlineData(ApplicationStatus.New)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Offer)]
    [InlineData(ApplicationStatus.Ignored)]
    public void A_status_with_nothing_to_chase_has_no_threshold(ApplicationStatus status)
    {
        ReminderPolicy.Default.ThresholdFor(status).ShouldBeNull();
    }

    [Fact]
    public void Thresholds_come_from_configuration_not_a_hard_coded_default()
    {
        var custom = new ReminderPolicy(new Dictionary<ApplicationStatus, TimeSpan>
        {
            [ApplicationStatus.Applied] = TimeSpan.FromDays(3),
        });

        custom.ThresholdFor(ApplicationStatus.Applied).ShouldBe(TimeSpan.FromDays(3));
        custom.ThresholdFor(ApplicationStatus.Interview).ShouldBeNull();
    }

    [Fact]
    public void A_non_positive_threshold_cannot_be_configured()
    {
        Should.Throw<ArgumentException>(() => new ReminderPolicy(new Dictionary<ApplicationStatus, TimeSpan>
        {
            [ApplicationStatus.Applied] = TimeSpan.Zero,
        }));
    }
}
