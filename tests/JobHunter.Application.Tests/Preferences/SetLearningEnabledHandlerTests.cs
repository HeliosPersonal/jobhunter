using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// T08 (done-when 4, AC-07): the Owner's runtime master switch over preference learning. Turning it off must
/// take effect on the next ranking and be stated on the next digest — so it cannot be a startup-only config
/// value; it is a persisted flag the Owner flips through the API reset/learning endpoint or the Telegram
/// override command. This handler is the shared write path behind both; the read path (<see cref="ILearningSwitch.IsEnabledAsync"/>)
/// is what <c>PreferenceModelQuery</c> and <c>DigestAssembler</c> consult.
/// </summary>
public sealed class SetLearningEnabledHandlerTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeLearningSwitch _switch = new();

    private SetLearningEnabledHandler CreateHandler() =>
        new(_switch, NullLogger<SetLearningEnabledHandler>.Instance);

    private Task<SetLearningEnabledOutcome> Handle(bool enabled) =>
        CreateHandler().Handle(new SetLearningEnabledCommand(enabled, OccurredAt), CancellationToken.None);

    [Fact]
    public async Task Turning_learning_off_persists_the_new_state_and_reports_the_change()
    {
        _switch.Enabled = true;

        var outcome = await Handle(enabled: false);

        outcome.Enabled.ShouldBeFalse();
        outcome.Changed.ShouldBeTrue();
        _switch.Enabled.ShouldBeFalse();       // persisted through the port
        _switch.SetCount.ShouldBe(1);
    }

    [Fact]
    public async Task Turning_learning_back_on_persists_and_reports_the_change()
    {
        _switch.Enabled = false;

        var outcome = await Handle(enabled: true);

        outcome.Enabled.ShouldBeTrue();
        outcome.Changed.ShouldBeTrue();
        _switch.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Setting_the_state_it_already_has_is_an_idempotent_no_op()
    {
        _switch.Enabled = true;

        var outcome = await Handle(enabled: true);

        outcome.Enabled.ShouldBeTrue();
        outcome.Changed.ShouldBeFalse();       // already in that state — nothing to persist
        _switch.SetCount.ShouldBe(0);          // no redundant write
    }

    private sealed class FakeLearningSwitch : ILearningSwitch
    {
        public bool Enabled { get; set; } = true;

        public int SetCount { get; private set; }

        public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Enabled);

        public Task SetAsync(bool enabled, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
        {
            Enabled = enabled;
            SetCount++;
            return Task.CompletedTask;
        }
    }
}
