using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/prefs</c> (catalogue §Profile · State read, F10 T08): the learned preferences the Owner can read before
/// deciding to switch any off — each weight rendered as the one plain sentence <see cref="WeightExplanation"/>
/// produces, quoting the count and total of the reaction that earned it (AC-03). Below the
/// <see cref="PreferenceModel.ActivationThreshold"/> signals learning needs before it shapes a ranking, it says
/// how many more are needed rather than rendering nothing, so the absence of learning is legible rather than
/// mysterious. It reads only the learned facts and their explanation — never the CV (which crosses exactly one
/// boundary, not this one). Every value reaches the reply through the one MarkdownV2 escaper.
/// </summary>
public sealed class PrefsCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private static readonly DateTimeOffset When = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ModelId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly IPreferenceModelRepository _models = Substitute.For<IPreferenceModelRepository>();
    private readonly IPreferenceStatusQuery _status = Substitute.For<IPreferenceStatusQuery>();

    private PrefsCommandHandler NewHandler() => new(
        new ActiveWeightsQuery(_models), _status, NullLogger<PrefsCommandHandler>.Instance);

    private static PreferenceWeight Weight(
        Dimension dimension, string value, decimal weight, decimal positiveRate, int supportingSignals) =>
        new(Guid.NewGuid(), ModelId, dimension, value, weight,
            [.. Enumerable.Range(0, supportingSignals).Select(_ => Guid.NewGuid())],
            positiveRate, When.AddDays(-3));

    private void SeedActiveModel(int signalCount, params PreferenceWeight[] weights)
    {
        var model = new PreferenceModel(ModelId, version: 1, signalCount, weights, When.AddDays(-3));
        if (weights.Length >= 0 && signalCount >= PreferenceModel.ActivationThreshold)
        {
            model.Activate(When.AddDays(-3));
        }

        _models.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(model);
    }

    [Fact]
    public async Task It_renders_each_weight_as_a_sentence_quoting_its_count_and_total()
    {
        // A negative pull the Owner passed on 34 of the last 38 times, and a positive one they engaged with.
        var salary = Weight(Dimension.SalaryBand, "sub-170k", -0.6m, positiveRate: 4m / 38m, supportingSignals: 38);
        var tech = Weight(Dimension.Technology, "Kafka", 0.4m, positiveRate: 30m / 40m, supportingSignals: 40);
        SeedActiveModel(signalCount: 250, salary, tech);
        _status.LatestAsync(Arg.Any<CancellationToken>()).Returns(new PreferenceStatus(250, HasActiveModel: true));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        var text = string.Join("\n", messages.Select(m => m.Text));
        // The dominant reaction's whole count and the total the sentence quotes (AC-03), for both weights.
        text.ShouldContain("34");
        text.ShouldContain("38");
        text.ShouldContain("30");
        text.ShouldContain("40");
    }

    [Fact]
    public async Task Below_the_threshold_it_says_how_many_more_signals_are_needed()
    {
        // No model has been activated; the latest fit sits below the evidence floor.
        _status.LatestAsync(Arg.Any<CancellationToken>()).Returns(new PreferenceStatus(143, HasActiveModel: false));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("143");
        text.ShouldContain("57"); // 200 - 143 still needed
    }

    [Fact]
    public async Task With_no_evidence_at_all_it_states_the_full_threshold_is_outstanding()
    {
        _status.LatestAsync(Arg.Any<CancellationToken>()).Returns((PreferenceStatus?)null);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("200");
    }

    [Fact]
    public async Task An_active_but_indifferent_model_says_no_strong_preferences_were_found()
    {
        // Enough evidence to activate, but the Owner reacted evenly — an indifferent profile earns no weights.
        SeedActiveModel(signalCount: 260);
        _status.LatestAsync(Arg.Any<CancellationToken>()).Returns(new PreferenceStatus(260, HasActiveModel: true));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("preferences", Case.Insensitive);
        text.ShouldNotContain("more"); // not a below-threshold message
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new PrefsCommandHandler(
            null!, _status, NullLogger<PrefsCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new PrefsCommandHandler(
            new ActiveWeightsQuery(_models), null!, NullLogger<PrefsCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new PrefsCommandHandler(
            new ActiveWeightsQuery(_models), _status, null!));
    }
}
