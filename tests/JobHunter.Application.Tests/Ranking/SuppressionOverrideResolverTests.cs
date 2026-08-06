using JobHunter.Application.Ranking;
using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Ranking;

/// <summary>
/// T07: the Owner override that outranks learning entirely (F7 data-model §suppression_overrides, AC-06). A
/// <see cref="SuppressionOverride"/> is a stated rule, not inferred evidence: for a matching <c>(dimension, value)</c>
/// it forces the outcome the model would otherwise decide. <see cref="SuppressionOverrideResolver"/> is pure — given
/// the model's suppression verdict (a reason or null), the job's <see cref="JobFacts"/> and the active overrides — it
/// returns the final verdict and records any tension where an override contradicted the model. The precedence rules:
/// <see cref="SuppressionMode.AlwaysSuppress"/> wins over both the model and a contradictory <see cref="SuppressionMode.NeverSuppress"/>
/// (of two conflicting Owner rules, hiding the job the Owner told us to hide is the safer resolution), and a
/// <see cref="SuppressionMode.NeverSuppress"/> vetoes a model suppression, recording the tension.
/// </summary>
public sealed class SuppressionOverrideResolverTests
{
    private static readonly DateTimeOffset When = new(2026, 8, 7, 7, 0, 0, TimeSpan.Zero);

    private static JobFacts FactsWith(Dimension dimension, params string[] values) =>
        JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>> { [dimension] = values });

    private static SuppressionOverride Override(Dimension dimension, string value, SuppressionMode mode) =>
        new(Guid.NewGuid(), dimension, value, mode, When);

    [Fact]
    public void With_no_overrides_the_model_verdict_passes_through_unchanged()
    {
        var facts = FactsWith(Dimension.Country, "DE");

        var result = SuppressionOverrideResolver.Resolve("Below presentation threshold", facts, []);

        result.Reason.ShouldBe("Below presentation threshold");
        result.Tension.ShouldBeNull();
    }

    [Fact]
    public void A_never_suppress_override_vetoes_a_model_suppression_and_records_the_tension()
    {
        var facts = FactsWith(Dimension.Country, "DE");
        var overrides = new[] { Override(Dimension.Country, "DE", SuppressionMode.NeverSuppress) };

        var result = SuppressionOverrideResolver.Resolve("Below presentation threshold", facts, overrides);

        result.Reason.ShouldBeNull();
        result.Tension.ShouldNotBeNull();
        result.Tension.ShouldContain("DE");
    }

    [Fact]
    public void A_never_suppress_override_that_does_not_contradict_records_no_tension()
    {
        // The model already wanted to show the job, so the NeverSuppress override agrees — no tension.
        var facts = FactsWith(Dimension.Country, "DE");
        var overrides = new[] { Override(Dimension.Country, "DE", SuppressionMode.NeverSuppress) };

        var result = SuppressionOverrideResolver.Resolve(modelReason: null, facts, overrides);

        result.Reason.ShouldBeNull();
        result.Tension.ShouldBeNull();
    }

    [Fact]
    public void An_always_suppress_override_hides_a_job_the_model_would_have_shown_with_its_own_reason()
    {
        var facts = FactsWith(Dimension.Country, "RU");
        var overrides = new[] { Override(Dimension.Country, "RU", SuppressionMode.AlwaysSuppress) };

        var result = SuppressionOverrideResolver.Resolve(modelReason: null, facts, overrides);

        result.Reason.ShouldNotBeNull();
        result.Reason.ShouldContain("RU");
        result.Tension.ShouldNotBeNull();
    }

    [Fact]
    public void An_always_suppress_override_keeps_the_override_reason_over_the_models()
    {
        // Already suppressed by the model, and the Owner also said always-hide: the override reason is the one
        // reported, because it names the deliberate rule rather than the generic threshold. No tension — they agree.
        var facts = FactsWith(Dimension.Country, "RU");
        var overrides = new[] { Override(Dimension.Country, "RU", SuppressionMode.AlwaysSuppress) };

        var result = SuppressionOverrideResolver.Resolve("Below presentation threshold", facts, overrides);

        result.Reason.ShouldNotBeNull();
        result.Reason.ShouldContain("RU");
        result.Tension.ShouldBeNull();
    }

    [Fact]
    public void Always_suppress_wins_over_a_contradictory_never_suppress()
    {
        // Two Owner rules collide on the same job (DE is both never- and always-suppress). Hiding the job the
        // Owner told us to hide is the safer resolution; the always-suppress reason is reported and the tension noted.
        var facts = FactsWith(Dimension.Country, "DE");
        var overrides = new[]
        {
            Override(Dimension.Country, "DE", SuppressionMode.NeverSuppress),
            Override(Dimension.Country, "DE", SuppressionMode.AlwaysSuppress),
        };

        var result = SuppressionOverrideResolver.Resolve(modelReason: null, facts, overrides);

        result.Reason.ShouldNotBeNull();
        result.Reason.ShouldContain("DE");
        result.Tension.ShouldNotBeNull();
    }

    [Fact]
    public void An_override_on_a_dimension_the_job_lacks_does_not_apply()
    {
        var facts = FactsWith(Dimension.Country, "DE");
        // The job has no CompanySize fact, so a CompanySize override cannot match it.
        var overrides = new[] { Override(Dimension.CompanySize, "SeriesA", SuppressionMode.AlwaysSuppress) };

        var result = SuppressionOverrideResolver.Resolve(modelReason: null, facts, overrides);

        result.Reason.ShouldBeNull();
        result.Tension.ShouldBeNull();
    }

    [Fact]
    public void An_override_matches_a_value_within_a_multi_value_dimension()
    {
        // Technology carries several values; an override on any one of them matches.
        var facts = FactsWith(Dimension.Technology, "Kafka", "Azure");
        var overrides = new[] { Override(Dimension.Technology, "Azure", SuppressionMode.AlwaysSuppress) };

        var result = SuppressionOverrideResolver.Resolve(modelReason: null, facts, overrides);

        result.Reason.ShouldNotBeNull();
        result.Reason.ShouldContain("Azure");
    }

    [Fact]
    public void Override_matching_is_case_insensitive_on_the_value()
    {
        var facts = FactsWith(Dimension.Country, "DE");
        var overrides = new[] { Override(Dimension.Country, "de", SuppressionMode.NeverSuppress) };

        var result = SuppressionOverrideResolver.Resolve("Below presentation threshold", facts, overrides);

        result.Reason.ShouldBeNull();
        result.Tension.ShouldNotBeNull();
    }

    [Fact]
    public void Null_facts_are_a_programmer_error()
    {
        Should.Throw<ArgumentNullException>(() =>
            SuppressionOverrideResolver.Resolve(null, null!, []));
    }

    [Fact]
    public void Null_overrides_are_a_programmer_error()
    {
        var facts = FactsWith(Dimension.Country, "DE");

        Should.Throw<ArgumentNullException>(() =>
            SuppressionOverrideResolver.Resolve(null, facts, null!));
    }
}
