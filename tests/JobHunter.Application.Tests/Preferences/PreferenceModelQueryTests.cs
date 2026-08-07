using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// F7 T06 C2: the real <see cref="IPreferenceModelQuery"/> that replaces the null default once F7 lands. It
/// loads the active model and its weights, each ranked job's current facts, and the active Profile's explicit
/// stances, then runs the pure <see cref="PreferenceComponentCalculator"/> to produce a per-job component for
/// F4 — mapping only the jobs the model has an opinion on, so F4 renormalises the preference weight away for
/// the rest. It stamps the active model's id so a refit is attributable (AC-04). Zero-database unit tests over
/// substituted ports; the disabled-weight-through-a-real-database path lives in the integration suite.
/// </summary>
public sealed class PreferenceModelQueryTests
{
    private static readonly Guid ModelId = Guid.CreateVersion7();
    private static readonly Guid KafkaJob = Guid.CreateVersion7();
    private static readonly Guid OtherJob = Guid.CreateVersion7();

    private readonly IPreferenceModelRepository _models = Substitute.For<IPreferenceModelRepository>();
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly IJobFactsSnapshotQuery _facts = Substitute.For<IJobFactsSnapshotQuery>();
    private readonly ILearningSwitch _learning = Substitute.For<ILearningSwitch>();

    private PreferenceModelQuery CreateQuery(bool learningEnabled = true)
    {
        _learning.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(learningEnabled);
        return new(_models, _profiles, _facts, _learning, NullLogger<PreferenceModelQuery>.Instance);
    }

    private static PreferenceModel ActiveModel(params PreferenceWeight[] weights)
    {
        var model = new PreferenceModel(ModelId, version: 1, signalCount: 250, weights, DateTimeOffset.UnixEpoch);
        model.Activate(DateTimeOffset.UnixEpoch);
        return model;
    }

    private static PreferenceWeight Weight(Dimension dimension, string value, decimal weight)
    {
        var w = new PreferenceWeight(
            Guid.CreateVersion7(), ModelId, dimension, value, weight,
            [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()],
            positiveRate: weight >= 0 ? 0.9m : 0.1m, createdAt: DateTimeOffset.UnixEpoch);
        return w;
    }

    private void FactsFor(Guid jobId, Dimension dimension, string value) =>
        _facts.SnapshotAsync(jobId, Arg.Any<CancellationToken>())
            .Returns(JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>> { [dimension] = [value] }));

    [Fact]
    public async Task With_no_active_model_it_returns_null_so_ranking_renormalises()
    {
        _models.FindActiveAsync(Arg.Any<CancellationToken>()).Returns((PreferenceModel?)null);

        var result = await CreateQuery().FindActiveAsync([KafkaJob], CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task With_learning_disabled_it_returns_null_even_when_a_model_is_active()
    {
        // AC-07: learning off means only explicit preferences apply. The active model is not even loaded —
        // ranking renormalises the preference weight away, exactly as if no model had ever been fitted.
        var result = await CreateQuery(learningEnabled: false)
            .FindActiveAsync([KafkaJob], CancellationToken.None);

        result.ShouldBeNull();
        await _models.DidNotReceive().FindActiveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_stamps_the_model_id_and_maps_only_jobs_the_model_has_an_opinion_on()
    {
        _models.FindActiveAsync(Arg.Any<CancellationToken>())
            .Returns(ActiveModel(Weight(Dimension.Technology, "Kafka", 0.5m)));
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns((Profile?)null);
        FactsFor(KafkaJob, Dimension.Technology, "Kafka");
        FactsFor(OtherJob, Dimension.Country, "NL");

        var result = await CreateQuery().FindActiveAsync([KafkaJob, OtherJob], CancellationToken.None);

        result.ShouldNotBeNull();
        result!.ModelId.ShouldBe(ModelId);
        result.ComponentByJob.ShouldContainKeyAndValue(KafkaJob, 0.75m);
        result.ComponentByJob.ShouldNotContainKey(OtherJob);   // no matching weight → F4 renormalises it away
    }

    [Fact]
    public async Task A_job_whose_facts_are_gone_is_simply_omitted()
    {
        _models.FindActiveAsync(Arg.Any<CancellationToken>())
            .Returns(ActiveModel(Weight(Dimension.Technology, "Kafka", 0.5m)));
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns((Profile?)null);
        _facts.SnapshotAsync(KafkaJob, Arg.Any<CancellationToken>()).Returns((JobFacts?)null);

        var result = await CreateQuery().FindActiveAsync([KafkaJob], CancellationToken.None);

        // A closed/superseded job has no facts to score; it is omitted, never scored on a stale join.
        result!.ComponentByJob.ShouldNotContainKey(KafkaJob);
    }

    [Fact]
    public async Task An_explicit_profile_country_preference_overrides_a_contradicting_learned_weight()
    {
        _models.FindActiveAsync(Arg.Any<CancellationToken>())
            .Returns(ActiveModel(Weight(Dimension.Country, "DE", -0.6m)));
        // The Owner explicitly prefers DE; the learned negative DE weight must be overridden (AC-05).
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(ProfileWithCountry("DE"));
        FactsFor(KafkaJob, Dimension.Country, "DE");

        var result = await CreateQuery().FindActiveAsync([KafkaJob], CancellationToken.None);

        // The contradicted weight is dropped, so the learned component no longer penalises the job: it maps to
        // the neutral midpoint (a conflict still produced a component; no negative pull survived).
        result!.ComponentByJob.ShouldContainKeyAndValue(KafkaJob, 0.5m);
    }

    [Fact]
    public async Task An_explicit_salary_floor_overrides_a_learned_positive_weight_on_a_below_floor_band()
    {
        // The model learned to reward the 90-120k band; the Owner then set a 150k USD floor. Every band wholly
        // below the floor becomes a negative explicit stance, so that learned positive pull is dropped (AC-05).
        _models.FindActiveAsync(Arg.Any<CancellationToken>())
            .Returns(ActiveModel(Weight(Dimension.SalaryBand, "90-120k", 0.6m)));
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(ProfileWithFloor(150_000m, "USD"));
        FactsFor(KafkaJob, Dimension.SalaryBand, "90-120k");

        var result = await CreateQuery().FindActiveAsync([KafkaJob], CancellationToken.None);

        // The learned reward is overridden: the component maps to the neutral midpoint, no positive pull survives.
        result!.ComponentByJob.ShouldContainKeyAndValue(KafkaJob, 0.5m);
    }

    [Fact]
    public async Task A_learned_weight_on_a_band_above_the_floor_is_untouched()
    {
        // The floor speaks only to bands wholly below it; a learned weight on the 150-180k band clears the 150k
        // floor and stands. Guards against an over-broad projection swallowing legitimate weights.
        _models.FindActiveAsync(Arg.Any<CancellationToken>())
            .Returns(ActiveModel(Weight(Dimension.SalaryBand, "150-180k", 0.6m)));
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(ProfileWithFloor(150_000m, "USD"));
        FactsFor(KafkaJob, Dimension.SalaryBand, "150-180k");

        var result = await CreateQuery().FindActiveAsync([KafkaJob], CancellationToken.None);

        // The positive pull survives: 0.5 + 0.6*0.5 = 0.8.
        result!.ComponentByJob.ShouldContainKeyAndValue(KafkaJob, 0.8m);
    }

    [Fact]
    public async Task A_non_usd_floor_projects_no_salary_band_stance()
    {
        // The learned bands are USD-only, so a EUR floor cannot honestly name one — it overrides nothing, exactly
        // as the band itself refuses to fabricate an FX rate. The learned reward stands.
        _models.FindActiveAsync(Arg.Any<CancellationToken>())
            .Returns(ActiveModel(Weight(Dimension.SalaryBand, "90-120k", 0.6m)));
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(ProfileWithFloor(150_000m, "EUR"));
        FactsFor(KafkaJob, Dimension.SalaryBand, "90-120k");

        var result = await CreateQuery().FindActiveAsync([KafkaJob], CancellationToken.None);

        result!.ComponentByJob.ShouldContainKeyAndValue(KafkaJob, 0.8m);
    }

    private static Profile ProfileWithCountry(string country) =>
        new(
            Guid.CreateVersion7(), isActive: true, displayName: "Owner",
            salaryFloor: null, salaryFloorCurrency: null, timezoneBand: TimezoneBand.EMEA,
            preferredCountries: [country], employmentTypes: [EmploymentType.FullTime],
            updatedAt: DateTimeOffset.UnixEpoch);

    private static Profile ProfileWithFloor(decimal amount, string currency) =>
        new(
            Guid.CreateVersion7(), isActive: true, displayName: "Owner",
            salaryFloor: amount, salaryFloorCurrency: currency, timezoneBand: TimezoneBand.EMEA,
            preferredCountries: [], employmentTypes: [EmploymentType.FullTime],
            updatedAt: DateTimeOffset.UnixEpoch);
}
