using JobHunter.Application.Applications;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;
using DeliveryOptions = JobHunter.Application.Delivery.DeliveryOptions;

namespace JobHunter.Application.Tests.Applications;

/// <summary>
/// The dependency-guard arm of <see cref="ReminderSweepHandler"/>: every injected collaborator, the policy and
/// the delivery options are null-checked in the field initialiser, so a missing dependency fails fast at
/// construction rather than at the first sweep.
/// </summary>
public sealed class ReminderSweepHandlerBranchTests
{
    private readonly IDueReminderQuery _dueReminders = Substitute.For<IDueReminderQuery>();
    private readonly IApplicationRepository _applications = Substitute.For<IApplicationRepository>();
    private readonly IReminderRenderer _renderer = Substitute.For<IReminderRenderer>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();
    private readonly ReminderPolicy _reminderPolicy = ReminderPolicy.Default;
    private readonly DeliveryOptions _delivery = new() { OwnerChatId = 1 };

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<ReminderSweepHandler>.Instance;

        Should.Throw<ArgumentNullException>(() => new ReminderSweepHandler(null!, _applications, _renderer, _notifier, _reminderPolicy, _delivery, logger));
        Should.Throw<ArgumentNullException>(() => new ReminderSweepHandler(_dueReminders, null!, _renderer, _notifier, _reminderPolicy, _delivery, logger));
        Should.Throw<ArgumentNullException>(() => new ReminderSweepHandler(_dueReminders, _applications, null!, _notifier, _reminderPolicy, _delivery, logger));
        Should.Throw<ArgumentNullException>(() => new ReminderSweepHandler(_dueReminders, _applications, _renderer, null!, _reminderPolicy, _delivery, logger));
        Should.Throw<ArgumentNullException>(() => new ReminderSweepHandler(_dueReminders, _applications, _renderer, _notifier, null!, _delivery, logger));
        Should.Throw<ArgumentNullException>(() => new ReminderSweepHandler(_dueReminders, _applications, _renderer, _notifier, _reminderPolicy, null!, logger));
        Should.Throw<ArgumentNullException>(() => new ReminderSweepHandler(_dueReminders, _applications, _renderer, _notifier, _reminderPolicy, _delivery, null!));
    }
}
