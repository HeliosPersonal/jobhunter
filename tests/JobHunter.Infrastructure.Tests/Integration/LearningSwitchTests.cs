using JobHunter.Application.Preferences;
using JobHunter.Infrastructure.Persistence.Preferences;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F7 T08 C4 (AC-07, done-when 4): the persisted, runtime-flippable master learning switch. A fresh database
/// has no state row, so the switch reports the configured seed default; a write persists across a new context
/// (the switch is the live source of truth, not the boot config); toggling flips it back. Single Owner: one
/// row, no per-tenant scoping. Requires Docker.
/// </summary>
public sealed class LearningSwitchTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 6, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task With_no_state_row_it_reports_the_configured_seed_default()
    {
        await using var database = await TestDatabase.CreateAsync();
        var switchOn = new PersistentLearningSwitch(database.CreateContext(), new LearningOptions { Enabled = true });
        var switchOff = new PersistentLearningSwitch(database.CreateContext(), new LearningOptions { Enabled = false });

        (await switchOn.IsEnabledAsync()).ShouldBeTrue();
        (await switchOff.IsEnabledAsync()).ShouldBeFalse();
    }

    [RequiresDockerFact]
    public async Task A_written_state_is_the_live_source_of_truth_over_the_seed_default()
    {
        await using var database = await TestDatabase.CreateAsync();

        // Seed default is on; the Owner turns learning off. A fresh switch — with the same on-by-default seed —
        // must read the persisted off, proving the store, not the boot config, is consulted at request time.
        var writer = new PersistentLearningSwitch(database.CreateContext(), new LearningOptions { Enabled = true });
        await writer.SetAsync(enabled: false, Now);

        var reader = new PersistentLearningSwitch(database.CreateContext(), new LearningOptions { Enabled = true });
        (await reader.IsEnabledAsync()).ShouldBeFalse();
    }

    [RequiresDockerFact]
    public async Task Toggling_it_back_on_persists_the_new_state()
    {
        await using var database = await TestDatabase.CreateAsync();

        await new PersistentLearningSwitch(database.CreateContext(), new LearningOptions { Enabled = true })
            .SetAsync(enabled: false, Now);
        await new PersistentLearningSwitch(database.CreateContext(), new LearningOptions { Enabled = true })
            .SetAsync(enabled: true, Now.AddHours(1));

        var reader = new PersistentLearningSwitch(database.CreateContext(), new LearningOptions { Enabled = false });
        (await reader.IsEnabledAsync()).ShouldBeTrue();
    }
}
