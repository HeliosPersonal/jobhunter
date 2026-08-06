using JobHunter.Application.Preferences;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// F7 T05: the weekly refit. On a <see cref="RecomputePreferencesDue"/> tick the learner loads the 180-day
/// signal window, fits it, and — only with at least <see cref="PreferenceModel.ActivationThreshold"/> signals
/// — inserts a new version and flips activation atomically, publishing <see cref="PreferenceModelUpdated"/>
/// (AC-01). Below the threshold no model is activated; a new inactive version records the reason on its notes
/// and the prior active model is left untouched (AC-02). These are zero-database unit tests over a fake
/// repository; the atomic single-commit and DST behaviour over a real database live in the integration suite.
/// </summary>
public sealed class PreferenceLearnerTests
{
    private static readonly DateTimeOffset FittedAt = new(2026, 8, 6, 3, 0, 0, TimeSpan.Zero);

    private readonly ISignalWindowQuery _signals = Substitute.For<ISignalWindowQuery>();
    private readonly FakePreferenceModelRepository _models = new();
    private readonly SequentialIdGenerator _ids = new();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private PreferenceLearner CreateLearner() =>
        new(_signals, _models, _ids, NullLogger<PreferenceLearner>.Instance);

    private Task Handle() =>
        CreateLearner().Handle(new RecomputePreferencesDue(FittedAt), _bus, CancellationToken.None);

    private void WindowOf(int count) =>
        _signals.LoadSince(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(SavedSignals(count));

    private static List<SignalFact> SavedSignals(int count)
    {
        var facts = JobFacts.Create(
            new Dictionary<Dimension, IReadOnlyList<string>> { [Dimension.Technology] = ["Kafka"] });
        var weight = SignalWeights.Default.WeightFor(SignalKind.Saved);
        return Enumerable.Range(0, count)
            .Select(_ => new SignalFact(Guid.CreateVersion7(), SignalKind.Saved, weight, facts, FittedAt.AddDays(-1)))
            .ToList();
    }

    [Fact]
    public async Task It_loads_the_window_from_the_reference_time_minus_the_configured_window()
    {
        WindowOf(0);

        await Handle();

        var expectedCutoff = FittedAt - new FittingOptions(FittedAt).Window;
        await _signals.Received(1).LoadSince(expectedCutoff, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task With_enough_signals_it_fits_activates_and_publishes()
    {
        WindowOf(PreferenceModel.ActivationThreshold);

        await Handle();

        var model = _models.Added.ShouldHaveSingleItem();
        model.IsActive.ShouldBeTrue();
        model.Version.ShouldBe(1);
        model.SignalCount.ShouldBe(PreferenceModel.ActivationThreshold);
        model.Weights.ShouldNotBeEmpty();
        model.Notes.ShouldBeNull();

        await _bus.Received(1).PublishAsync(Arg.Is<PreferenceModelUpdated>(e =>
            e != null
            && e.ModelId == model.Id
            && e.Version == model.Version
            && e.SignalCount == model.SignalCount
            && e.FittedAt == FittedAt));
    }

    [Fact]
    public async Task Below_the_threshold_it_records_insufficient_evidence_and_activates_nothing()
    {
        WindowOf(PreferenceModel.ActivationThreshold - 1);

        await Handle();

        var model = _models.Added.ShouldHaveSingleItem();
        model.IsActive.ShouldBeFalse();
        model.SignalCount.ShouldBe(PreferenceModel.ActivationThreshold - 1);
        model.Weights.ShouldBeEmpty();
        model.Notes.ShouldBe($"insufficient evidence: {PreferenceModel.ActivationThreshold - 1} signals");

        await _bus.DidNotReceive().PublishAsync(Arg.Any<PreferenceModelUpdated>());
    }

    [Theory]
    [InlineData(199, false)]
    [InlineData(200, true)]
    public async Task The_threshold_boundary_decides_activation(int signalCount, bool activates)
    {
        WindowOf(signalCount);

        await Handle();

        _models.Added.ShouldHaveSingleItem().IsActive.ShouldBe(activates);
    }

    [Fact]
    public async Task It_deactivates_the_prior_active_model_and_leaves_it_queryable()
    {
        var prior = new PreferenceModel(
            Guid.CreateVersion7(), version: 1, signalCount: 250, [], FittedAt.AddDays(-7));
        prior.Activate(FittedAt.AddDays(-7));
        _models.Seed(prior);
        WindowOf(PreferenceModel.ActivationThreshold);

        await Handle();

        prior.IsActive.ShouldBeFalse();                       // deactivated
        _models.All.ShouldContain(prior);                     // still queryable — rollback is a flag change
        _models.All.Count(m => m.IsActive).ShouldBe(1);       // exactly one active
        _models.All.Single(m => m.IsActive).Version.ShouldBe(2);
    }

    [Fact]
    public async Task The_new_version_is_one_higher_than_the_latest()
    {
        _models.Seed(new PreferenceModel(Guid.CreateVersion7(), version: 3, signalCount: 10, [], FittedAt.AddDays(-7)));
        WindowOf(PreferenceModel.ActivationThreshold);

        await Handle();

        _models.Added.ShouldHaveSingleItem().Version.ShouldBe(4);
    }

    [Fact]
    public async Task It_commits_exactly_once_so_the_flip_is_atomic()
    {
        var prior = new PreferenceModel(Guid.CreateVersion7(), version: 1, signalCount: 250, [], FittedAt.AddDays(-7));
        prior.Activate(FittedAt.AddDays(-7));
        _models.Seed(prior);
        WindowOf(PreferenceModel.ActivationThreshold);

        await Handle();

        _models.SaveCount.ShouldBe(1);
    }

    /// <summary>
    /// An in-memory <see cref="IPreferenceModelRepository"/>: <c>Add</c> stages, <c>SaveChangesAsync</c>
    /// commits the staged models into the queryable set in one call, so a test can assert the deactivate/activate
    /// flip lands in a single commit (atomicity, done-when 4) and the prior version stays queryable (done-when 5).
    /// </summary>
    private sealed class FakePreferenceModelRepository : IPreferenceModelRepository
    {
        private readonly List<PreferenceModel> _committed = [];
        private readonly List<PreferenceModel> _staged = [];

        /// <summary>The models the learner staged through <see cref="Add"/> — never the seeded prior versions.</summary>
        public List<PreferenceModel> Added { get; } = [];

        /// <summary>Everything queryable after commit: seeded prior versions plus staged models that were saved.</summary>
        public IReadOnlyList<PreferenceModel> All => _committed;

        public int SaveCount { get; private set; }

        public void Seed(PreferenceModel model) => _committed.Add(model);

        public void Add(PreferenceModel model)
        {
            _staged.Add(model);
            Added.Add(model);
        }

        public Task<PreferenceModel?> FindActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_committed.FirstOrDefault(m => m.IsActive));

        public Task<int?> LatestVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_committed.Count == 0 ? (int?)null : _committed.Max(m => m.Version));

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            _committed.AddRange(_staged);
            var n = _staged.Count;
            _staged.Clear();
            return Task.FromResult(n);
        }
    }
}
