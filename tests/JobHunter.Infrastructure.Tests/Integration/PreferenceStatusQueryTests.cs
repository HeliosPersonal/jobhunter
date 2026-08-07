using JobHunter.Domain.Preferences;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// The read behind <c>/prefs</c> (F10 T08): <see cref="PreferenceStatusQuery"/> returns the latest fitted
/// model's signal count and whether it is active, so the command can say how many more signals learning needs
/// before it shapes a ranking. It is deliberately separate from the write repository, whose active read returns
/// nothing when no model has been activated — the exact case where <c>/prefs</c> still needs the latest count.
/// Requires Docker.
/// </summary>
public sealed class PreferenceStatusQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 6, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task With_no_model_fitted_it_returns_null()
    {
        await using var database = await TestDatabase.CreateAsync();

        var status = await new PreferenceStatusQuery(
            new NpgsqlConnectionFactory(database.ConnectionString)).LatestAsync();

        status.ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task It_reports_the_latest_fit_signal_count_and_inactive_when_below_the_floor()
    {
        await using var database = await TestDatabase.CreateAsync();

        // A fit that fell short of the activation floor: inserted inactive, no Activate call.
        var repo = new PreferenceModelRepository(database.CreateContext());
        repo.Add(new PreferenceModel(Guid.CreateVersion7(), version: 1, signalCount: 143, [], Now));
        await repo.SaveChangesAsync();

        var status = await new PreferenceStatusQuery(
            new NpgsqlConnectionFactory(database.ConnectionString)).LatestAsync();

        status.ShouldNotBeNull();
        status.SignalCount.ShouldBe(143);
        status.HasActiveModel.ShouldBeFalse();
    }

    [RequiresDockerFact]
    public async Task It_reports_the_highest_version_and_active_when_a_model_is_active()
    {
        await using var database = await TestDatabase.CreateAsync();

        // v1 fitted then superseded by an active v2 with more evidence: the latest (highest version) is reported.
        var setup = new PreferenceModelRepository(database.CreateContext());
        setup.Add(new PreferenceModel(Guid.CreateVersion7(), version: 1, signalCount: 210, [], Now));
        await setup.SaveChangesAsync();

        var v2 = new PreferenceModel(Guid.CreateVersion7(), version: 2, signalCount: 260, [], Now.AddDays(7));
        v2.Activate(Now.AddDays(7));
        var refit = new PreferenceModelRepository(database.CreateContext());
        refit.Add(v2);
        await refit.SaveChangesAsync();

        var status = await new PreferenceStatusQuery(
            new NpgsqlConnectionFactory(database.ConnectionString)).LatestAsync();

        status.ShouldNotBeNull();
        status.SignalCount.ShouldBe(260);
        status.HasActiveModel.ShouldBeTrue();
    }
}
