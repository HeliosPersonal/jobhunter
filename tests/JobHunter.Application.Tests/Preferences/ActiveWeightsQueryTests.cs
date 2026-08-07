using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// T08 C6 (AC-03, AC-06): the read the Owner sees before disabling anything — the active model's learned
/// weights, each with the id needed to switch it off and the one-sentence explanation of why it exists. It
/// composes the active <see cref="PreferenceModel"/> and the shared <see cref="WeightExplanation"/> so the
/// API and Telegram surfaces render identical sentences. Strongest pull first, disabled weights still shown
/// (they stay inspectable), and no active model is an empty list, never a fault.
/// </summary>
public sealed class ActiveWeightsQueryTests
{
    private static readonly DateTimeOffset When = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ModelId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly FakeModels _models = new();

    private ActiveWeightsQuery CreateQuery() => new(_models);

    private static PreferenceWeight Weight(
        Guid id, Dimension dimension, string value, decimal weight, decimal positiveRate = 0.2m) =>
        new(id, ModelId, dimension, value, weight,
            [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], positiveRate, When.AddDays(-3));

    private void SeedActiveModelWith(params PreferenceWeight[] weights)
    {
        var model = new PreferenceModel(ModelId, version: 1, signalCount: 250, weights, When.AddDays(-3));
        model.Activate(When.AddDays(-3));
        _models.Seed(model);
    }

    [Fact]
    public async Task It_lists_each_active_weight_with_its_id_and_explanation()
    {
        var weightId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var weight = Weight(weightId, Dimension.Country, "DE", -0.6m);
        SeedActiveModelWith(weight);

        var listed = await CreateQuery().ActiveAsync(CancellationToken.None);

        var only = listed.ShouldHaveSingleItem();
        only.WeightId.ShouldBe(weightId);
        only.Dimension.ShouldBe(Dimension.Country);
        only.Value.ShouldBe("DE");
        only.Weight.ShouldBe(-0.6m);
        only.Disabled.ShouldBeFalse();
        only.Explanation.ShouldBe(WeightExplanation.Describe(weight));
    }

    [Fact]
    public async Task Weights_come_back_strongest_pull_first()
    {
        var weak = Weight(Guid.NewGuid(), Dimension.Technology, "Kafka", 0.2m);
        var strong = Weight(Guid.NewGuid(), Dimension.Country, "DE", -0.8m);
        var middle = Weight(Guid.NewGuid(), Dimension.SalaryBand, "sub-170k", -0.5m);
        SeedActiveModelWith(weak, strong, middle);

        var listed = await CreateQuery().ActiveAsync(CancellationToken.None);

        listed.Select(w => w.Value).ShouldBe(["DE", "sub-170k", "Kafka"]);
    }

    [Fact]
    public async Task A_disabled_weight_is_still_listed_and_flagged()
    {
        var disabled = Weight(Guid.NewGuid(), Dimension.Country, "DE", -0.6m);
        disabled.Disable(When.AddDays(-1));
        SeedActiveModelWith(disabled);

        var listed = await CreateQuery().ActiveAsync(CancellationToken.None);

        listed.ShouldHaveSingleItem().Disabled.ShouldBeTrue();
    }

    [Fact]
    public async Task With_no_active_model_the_list_is_empty()
    {
        var listed = await CreateQuery().ActiveAsync(CancellationToken.None);

        listed.ShouldBeEmpty();
    }

    /// <summary>An in-memory repository over the active model, mirroring the disable handler's fake.</summary>
    private sealed class FakeModels : IPreferenceModelRepository
    {
        private readonly List<PreferenceModel> _committed = [];

        public void Seed(PreferenceModel model) => _committed.Add(model);

        public void Add(PreferenceModel model) => _committed.Add(model);

        public Task<PreferenceModel?> FindActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_committed.FirstOrDefault(m => m.IsActive));

        public Task<int?> LatestVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_committed.Count == 0 ? (int?)null : _committed.Max(m => m.Version));

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
