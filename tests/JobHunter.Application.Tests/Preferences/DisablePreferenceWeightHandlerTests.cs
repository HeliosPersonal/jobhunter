using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// T08 (AC-06): the Owner disabling a specific learned weight. The write path loads the active
/// <see cref="PreferenceModel"/>, switches the addressed weight off, and commits — the exclusion in
/// <see cref="PreferenceComponentCalculator"/> then keeps it out of the very next ranking. A weight is
/// never deleted (it stays inspectable), the operation is idempotent, and an unknown id is a value-typed
/// refusal, not an exception (coding-standards §4). The outcome echoes the weight's one-sentence explanation
/// so the caller can confirm exactly what was switched off.
/// </summary>
public sealed class DisablePreferenceWeightHandlerTests
{
    private static readonly DateTimeOffset When = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ModelId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly FakeModels _models = new();

    private DisablePreferenceWeightHandler CreateHandler() =>
        new(_models, NullLogger<DisablePreferenceWeightHandler>.Instance);

    private static PreferenceWeight Weight(Guid id, Dimension dimension = Dimension.Country, string value = "DE") =>
        new(id, ModelId, dimension, value, -0.6m, [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], 0.2m, When.AddDays(-3));

    private PreferenceModel SeedActiveModelWith(params PreferenceWeight[] weights)
    {
        var model = new PreferenceModel(ModelId, version: 1, signalCount: 250, weights, When.AddDays(-3));
        model.Activate(When.AddDays(-3));
        _models.Seed(model);
        return model;
    }

    [Fact]
    public async Task Disabling_a_weight_switches_it_off_records_when_and_commits_once()
    {
        var weightId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var weight = Weight(weightId);
        SeedActiveModelWith(weight, Weight(Guid.NewGuid(), Dimension.Technology, "Kafka"));

        var outcome = await CreateHandler().Handle(
            new DisablePreferenceWeightCommand(weightId, When), CancellationToken.None);

        outcome.Result.ShouldBe(DisablePreferenceWeightResult.Disabled);
        outcome.Explanation.ShouldBe(WeightExplanation.Describe(weight));
        weight.Disabled.ShouldBeTrue();
        weight.DisabledAt.ShouldBe(When);
        _models.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_weight_id_is_refused_as_a_value_and_nothing_is_committed()
    {
        SeedActiveModelWith(Weight(Guid.NewGuid()));

        var outcome = await CreateHandler().Handle(
            new DisablePreferenceWeightCommand(Guid.NewGuid(), When), CancellationToken.None);

        outcome.Result.ShouldBe(DisablePreferenceWeightResult.WeightNotFound);
        outcome.Explanation.ShouldBeNull();
        _models.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task With_no_active_model_the_weight_cannot_be_found()
    {
        var outcome = await CreateHandler().Handle(
            new DisablePreferenceWeightCommand(Guid.NewGuid(), When), CancellationToken.None);

        outcome.Result.ShouldBe(DisablePreferenceWeightResult.WeightNotFound);
        _models.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Disabling_an_already_disabled_weight_is_idempotent_and_keeps_the_first_timestamp()
    {
        var weightId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        var weight = Weight(weightId);
        weight.Disable(When.AddDays(-1));
        SeedActiveModelWith(weight);

        var outcome = await CreateHandler().Handle(
            new DisablePreferenceWeightCommand(weightId, When), CancellationToken.None);

        outcome.Result.ShouldBe(DisablePreferenceWeightResult.Disabled);
        weight.Disabled.ShouldBeTrue();
        weight.DisabledAt.ShouldBe(When.AddDays(-1));   // first switch-off stands
        // Still committed so a redelivered request is safe, but the timestamp the explanation refers to is stable.
        _models.SaveCount.ShouldBe(1);
    }

    /// <summary>An in-memory repository whose active model's weights are mutated in place, so a disable is observable.</summary>
    private sealed class FakeModels : IPreferenceModelRepository
    {
        private readonly List<PreferenceModel> _committed = [];

        public int SaveCount { get; private set; }

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
