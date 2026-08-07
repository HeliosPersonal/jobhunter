using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// T08 (done-when 3): a full reset deactivates the active model without deleting any signal — the Owner's
/// escape hatch when learning has drifted. It is the coarse counterpart to <see cref="DisablePreferenceWeightHandler"/>:
/// where disable switches off one weight, reset switches off the whole model, leaving F4 to fall back to the
/// explicit-preference floor until the next refit rebuilds evidence. No <c>PreferenceModel</c> or
/// <c>Signal</c> row is deleted — the model stays queryable, and the signals the reset was a reaction to are
/// exactly the evidence a future refit needs.
/// </summary>
public sealed class ResetPreferenceModelHandlerTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeModels _models = new();

    private ResetPreferenceModelHandler CreateHandler() =>
        new(_models, NullLogger<ResetPreferenceModelHandler>.Instance);

    private Task<ResetPreferenceModelOutcome> Handle() =>
        CreateHandler().Handle(new ResetPreferenceModelCommand(OccurredAt), CancellationToken.None);

    private static PreferenceModel ActiveModel()
    {
        var model = new PreferenceModel(Guid.NewGuid(), version: 4, signalCount: 250, [], OccurredAt.AddDays(-7));
        model.Activate(OccurredAt.AddDays(-7));
        return model;
    }

    [Fact]
    public async Task It_deactivates_the_active_model_and_reports_the_version_it_switched_off()
    {
        var active = ActiveModel();
        _models.Seed(active);

        var outcome = await Handle();

        outcome.Result.ShouldBe(ResetPreferenceModelResult.Reset);
        outcome.DeactivatedVersion.ShouldBe(4);
        active.IsActive.ShouldBeFalse();
        _models.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task It_leaves_the_deactivated_model_queryable_and_deletes_nothing()
    {
        var active = ActiveModel();
        _models.Seed(active);

        await Handle();

        _models.All.ShouldContain(active);   // a flag change, never a delete
        _models.Deleted.ShouldBe(0);
    }

    [Fact]
    public async Task With_no_active_model_it_reports_nothing_to_reset_and_does_not_commit()
    {
        var outcome = await Handle();

        outcome.Result.ShouldBe(ResetPreferenceModelResult.NothingActive);
        outcome.DeactivatedVersion.ShouldBeNull();
        _models.SaveCount.ShouldBe(0);
    }

    private sealed class FakeModels : IPreferenceModelRepository
    {
        private readonly List<PreferenceModel> _committed = [];

        public IReadOnlyList<PreferenceModel> All => _committed;

        public int SaveCount { get; private set; }

        public int Deleted { get; private set; }

        public void Seed(PreferenceModel model) => _committed.Add(model);

        public void Add(PreferenceModel model) => _committed.Add(model);

        public Task<PreferenceModel?> FindActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_committed.FirstOrDefault(m => m.IsActive));

        public Task<int?> LatestVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_committed.Count == 0 ? (int?)null : _committed.Max(m => m.Version));

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }
}
