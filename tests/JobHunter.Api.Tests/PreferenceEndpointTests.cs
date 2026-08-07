using System.Net;
using JobHunter.Api.Endpoints;
using JobHunter.Application.Preferences;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Reporting;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The F7 preference-learning endpoints end-to-end (T08 C6, AC-10): the two reads — the active model's
/// learned weights (AC-03) and the latest Run's hidden jobs (risk D3) — declare <c>jobhunter:read</c>; the
/// three owner writes — disable a weight (AC-06), reset the model (done-when 3) and toggle learning (AC-07) —
/// declare <c>jobhunter:admin</c>. A read token on a write is a 403 and an anonymous call a 401 (the
/// endpoint-convention gate); an unknown weight and a reset with nothing active answer 404 as problem+json. No
/// response carries a CV-derived value or a match reason (the CV crosses exactly one boundary, not this one).
/// </summary>
public sealed class PreferenceEndpointTests : IClassFixture<EndpointsHostFactory>
{
    private static readonly Guid ModelId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly EndpointsHostFactory _factory;

    public PreferenceEndpointTests(EndpointsHostFactory factory) => _factory = factory;

    private static PreferenceWeight Weight(Guid id, Dimension dimension = Dimension.Country, string value = "DE") =>
        new(id, ModelId, dimension, value, -0.6m,
            [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], 0.2m, new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero));

    private static PreferenceModel ActiveModelWith(params PreferenceWeight[] weights)
    {
        var model = new PreferenceModel(ModelId, version: 3, signalCount: 250, weights, new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero));
        model.Activate(new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero));
        return model;
    }

    // --- Weights (read) ----------------------------------------------------------------------------

    [Fact]
    public async Task Weights_returns_each_active_weight_with_its_id_and_explanation()
    {
        var weightId = Guid.NewGuid();
        var weight = Weight(weightId);
        _factory.PreferenceModels.FindActiveAsync(Arg.Any<CancellationToken>())
            .Returns(ActiveModelWith(weight));

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/preferences/weights", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<IReadOnlyList<LearnedWeightResponse>>();
        body.ShouldNotBeNull();
        var only = body.ShouldHaveSingleItem();
        only.WeightId.ShouldBe(weightId);
        only.Dimension.ShouldBe("Country");
        only.Value.ShouldBe("DE");
        only.Explanation.ShouldBe(WeightExplanation.Describe(weight));
    }

    [Fact]
    public async Task Weights_without_a_token_is_a_401()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/preferences/weights", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Hidden (read) -----------------------------------------------------------------------------

    [Fact]
    public async Task Hidden_returns_the_suppressed_jobs_with_their_reasons()
    {
        var jobId = Guid.NewGuid();
        _factory.HiddenJobs.HiddenAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new HiddenJob(jobId, "Staff SRE", "Acme", 30m, "Salary below 170k EUR")]);

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/preferences/hidden", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<IReadOnlyList<HiddenJobResponse>>();
        body.ShouldNotBeNull();
        var only = body.ShouldHaveSingleItem();
        only.JobId.ShouldBe(jobId);
        only.Title.ShouldBe("Staff SRE");
        only.SuppressionReason.ShouldBe("Salary below 170k EUR");
    }

    // --- Disable a weight (admin) ------------------------------------------------------------------

    [Fact]
    public async Task Disable_switches_the_weight_off_and_echoes_its_explanation()
    {
        var weightId = Guid.NewGuid();
        var weight = Weight(weightId);
        _factory.PreferenceModels.FindActiveAsync(Arg.Any<CancellationToken>())
            .Returns(ActiveModelWith(weight));

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsync(
            new Uri($"/api/preferences/weights/{weightId}/disable", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DisableWeightResponse>();
        body.ShouldNotBeNull();
        body.Explanation.ShouldBe(WeightExplanation.Describe(weight));
        weight.Disabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Disable_of_an_unknown_weight_is_a_404()
    {
        _factory.PreferenceModels.FindActiveAsync(Arg.Any<CancellationToken>())
            .Returns(ActiveModelWith(Weight(Guid.NewGuid())));

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsync(
            new Uri($"/api/preferences/weights/{Guid.NewGuid()}/disable", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Disable_with_only_a_read_token_is_a_403()
    {
        using var client = _factory.OwnerClient();
        var response = await client.PostAsync(
            new Uri($"/api/preferences/weights/{Guid.NewGuid()}/disable", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Reset the model (admin) -------------------------------------------------------------------

    [Fact]
    public async Task Reset_deactivates_the_active_model_and_reports_its_version()
    {
        _factory.PreferenceModels.FindActiveAsync(Arg.Any<CancellationToken>())
            .Returns(ActiveModelWith(Weight(Guid.NewGuid())));

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsync(new Uri("/api/preferences/reset", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ResetModelResponse>();
        body.ShouldNotBeNull();
        body.DeactivatedVersion.ShouldBe(3);
    }

    [Fact]
    public async Task Reset_with_nothing_active_is_a_404()
    {
        _factory.PreferenceModels.FindActiveAsync(Arg.Any<CancellationToken>())
            .Returns((PreferenceModel?)null);

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsync(new Uri("/api/preferences/reset", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Reset_with_only_a_read_token_is_a_403()
    {
        using var client = _factory.OwnerClient();
        var response = await client.PostAsync(new Uri("/api/preferences/reset", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Toggle learning (admin) -------------------------------------------------------------------

    [Fact]
    public async Task Learning_off_flips_the_switch_and_reports_the_new_state()
    {
        _factory.LearningSwitch.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PutAsJsonAsync(
            new Uri("/api/preferences/learning", UriKind.Relative), new SetLearningRequest(false));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LearningStateResponse>();
        body.ShouldNotBeNull();
        body.Enabled.ShouldBeFalse();
        body.Changed.ShouldBeTrue();
        await _factory.LearningSwitch.Received(1).SetAsync(false, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Learning_toggle_with_only_a_read_token_is_a_403()
    {
        using var client = _factory.OwnerClient();
        var response = await client.PutAsJsonAsync(
            new Uri("/api/preferences/learning", UriKind.Relative), new SetLearningRequest(false));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
