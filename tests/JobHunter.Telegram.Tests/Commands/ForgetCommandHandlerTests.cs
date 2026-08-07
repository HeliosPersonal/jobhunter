using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/forget &lt;dimension&gt;</c> (catalogue §Profile, F10 T08): switches the learned weight(s) for a named
/// dimension off, through the same <see cref="DisablePreferenceWeightHandler"/> the API disable endpoint uses,
/// so there is one write path. The reply states plainly that it takes effect on the <strong>next ranking, not
/// mid-Run</strong> (AC-05), so a single Run's ordering stays internally consistent. With no argument it lists
/// the dimensions carrying an active weight so the Owner can pick, never failing. An unknown dimension names the
/// valid ones. It touches no CV (the CV crosses exactly one boundary, not this one).
/// </summary>
public sealed class ForgetCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ModelId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly IPreferenceModelRepository _models = Substitute.For<IPreferenceModelRepository>();
    private readonly FakeClock _clock = new(Now);

    private ForgetCommandHandler NewHandler() => new(
        new ActiveWeightsQuery(_models),
        new DisablePreferenceWeightHandler(_models, NullLogger<DisablePreferenceWeightHandler>.Instance),
        _clock,
        NullLogger<ForgetCommandHandler>.Instance);

    private static PreferenceWeight Weight(Guid id, Dimension dimension, string value, decimal weight) =>
        new(id, ModelId, dimension, value, weight,
            [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], positiveRate: 0.2m, Now.AddDays(-3));

    private void SeedActiveModelWith(params PreferenceWeight[] weights)
    {
        var model = new PreferenceModel(ModelId, version: 1, signalCount: 250, weights, Now.AddDays(-3));
        model.Activate(Now.AddDays(-3));
        _models.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(model);
    }

    [Fact]
    public async Task It_disables_the_weight_for_the_named_dimension_and_states_it_takes_effect_next_ranking()
    {
        var salary = Weight(Guid.NewGuid(), Dimension.SalaryBand, "sub-170k", -0.6m);
        SeedActiveModelWith(salary, Weight(Guid.NewGuid(), Dimension.Country, "DE", -0.4m));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "salary"));

        salary.Disabled.ShouldBeTrue();
        await _models.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
        var text = string.Join("\n", messages.Select(m => m.Text));
        text.ShouldContain("next", Case.Insensitive);
        text.ShouldContain("run", Case.Insensitive); // "not mid-Run"
    }

    [Fact]
    public async Task It_disables_every_active_weight_in_the_dimension()
    {
        var de = Weight(Guid.NewGuid(), Dimension.Country, "DE", -0.4m);
        var nl = Weight(Guid.NewGuid(), Dimension.Country, "NL", 0.5m);
        SeedActiveModelWith(de, nl, Weight(Guid.NewGuid(), Dimension.SalaryBand, "sub-170k", -0.6m));

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "country"));

        de.Disabled.ShouldBeTrue();
        nl.Disabled.ShouldBeTrue();
    }

    [Fact]
    public async Task With_no_argument_it_lists_the_dimensions_that_have_an_active_weight()
    {
        SeedActiveModelWith(
            Weight(Guid.NewGuid(), Dimension.SalaryBand, "sub-170k", -0.6m),
            Weight(Guid.NewGuid(), Dimension.Country, "DE", -0.4m));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("salary", Case.Insensitive);
        text.ShouldContain("country", Case.Insensitive);
        await _models.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_dimension_names_the_ones_that_can_be_forgotten()
    {
        SeedActiveModelWith(Weight(Guid.NewGuid(), Dimension.SalaryBand, "sub-170k", -0.6m));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "moon-phase"));

        messages.ShouldHaveSingleItem().Text.ShouldContain("salary", Case.Insensitive);
        await _models.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_dimension_with_no_active_weight_is_reported_not_disabled()
    {
        // The dimension name is valid, but the model learned nothing about it.
        SeedActiveModelWith(Weight(Guid.NewGuid(), Dimension.SalaryBand, "sub-170k", -0.6m));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "country"));

        messages.ShouldHaveSingleItem().Text.ShouldContain("nothing", Case.Insensitive);
        await _models.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task With_no_learned_preferences_at_all_it_says_so_rather_than_offering_an_empty_list()
    {
        _models.FindActiveAsync(Arg.Any<CancellationToken>()).Returns((PreferenceModel?)null);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("no", Case.Insensitive);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var weights = new ActiveWeightsQuery(_models);
        var disable = new DisablePreferenceWeightHandler(_models, NullLogger<DisablePreferenceWeightHandler>.Instance);
        Should.Throw<ArgumentNullException>(() => new ForgetCommandHandler(null!, disable, _clock, NullLogger<ForgetCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new ForgetCommandHandler(weights, null!, _clock, NullLogger<ForgetCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new ForgetCommandHandler(weights, disable, null!, NullLogger<ForgetCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new ForgetCommandHandler(weights, disable, _clock, null!));
    }
}
