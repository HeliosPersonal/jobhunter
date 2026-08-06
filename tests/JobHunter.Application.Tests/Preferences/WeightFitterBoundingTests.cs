using JobHunter.Application.Preferences;
using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// F7 T04 C2: the bounding-and-normalisation half of the fitter, and the property suite behind AC-09/QG-3.
/// The raw fit (C1) produces a signed weight per <c>(dimension, value)</c> in <c>[-1, +1]</c>; this stage
/// caps each <em>dimension's</em> total contribution at <see cref="FittingOptions.MaxDimensionShare"/> of the
/// preference component and normalises across dimensions, so an overwhelming one-sided pattern in a single
/// dimension can never become the only thing that matters (SAD §8, ADR-F7-0001). The bound is asserted here
/// not on one hand-built case but over a family of adversarial distributions — the property the test-plan
/// calls out (AC-09 <c>OneSidedEvidence_ProducesBoundedEffect</c>).
/// </summary>
public sealed class WeightFitterBoundingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly FittingOptions Options = new(Now);

    /// <summary>A signal whose job facts carry one value in each of several dimensions.</summary>
    private static SignalFact MultiFact(
        SignalKind kind, double ageDays, IReadOnlyDictionary<Dimension, string> values)
    {
        var facts = JobFacts.Create(
            values.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)[kv.Value]));
        return new SignalFact(
            Guid.CreateVersion7(),
            kind,
            SignalWeights.Default.WeightFor(kind),
            facts,
            Now.AddDays(-ageDays));
    }

    private static List<SignalFact> Many(int count, Func<int, SignalFact> make) =>
        Enumerable.Range(0, count).Select(make).ToList();

    /// <summary>The total absolute weight a dimension carries — its contribution mass to the component.</summary>
    private static decimal DimensionMass(FittedModel model, Dimension dimension) =>
        model.Weights.Where(w => w.Dimension == dimension).Sum(w => Math.Abs(w.Weight));

    [Fact]
    public void No_dimension_exceeds_its_share_of_the_component_when_others_carry_evidence()
    {
        // Country is wildly one-sided (all ignores for DE) while Technology and RemotePolicy carry milder,
        // opposite-signed evidence. Un-bounded, Country would dominate; the cap holds it to its share.
        var signals = new List<SignalFact>();
        signals.AddRange(Many(20, _ => MultiFact(SignalKind.Ignored, 3, new Dictionary<Dimension, string>
        {
            [Dimension.Country] = "DE",
            [Dimension.Technology] = "Java",
            [Dimension.RemotePolicy] = "Onsite",
        })));
        signals.AddRange(Many(5, _ => MultiFact(SignalKind.Saved, 3, new Dictionary<Dimension, string>
        {
            [Dimension.Technology] = "Kafka",
            [Dimension.RemotePolicy] = "Remote",
        })));

        var model = WeightFitter.Fit(signals, Options);

        foreach (var dimension in model.Weights.Select(w => w.Dimension).Distinct())
        {
            DimensionMass(model, dimension).ShouldBeLessThanOrEqualTo(Options.MaxDimensionShare + 0.0001m);
        }
    }

    [Fact]
    public void Dimension_masses_sum_to_one_when_at_least_three_dimensions_carry_weight()
    {
        // Three dimensions, each clearly one-sided: normalisation makes the total contribution mass a unit,
        // so the component is a proper weighted blend rather than an unbounded sum.
        var signals = new List<SignalFact>();
        signals.AddRange(Many(6, _ => MultiFact(SignalKind.Saved, 2, new Dictionary<Dimension, string>
        {
            [Dimension.Technology] = "Rust",
            [Dimension.Country] = "NL",
            [Dimension.RemotePolicy] = "Remote",
        })));
        signals.AddRange(Many(6, _ => MultiFact(SignalKind.Ignored, 2, new Dictionary<Dimension, string>
        {
            [Dimension.Technology] = "PHP",
            [Dimension.Country] = "IN",
            [Dimension.RemotePolicy] = "Onsite",
        })));

        var model = WeightFitter.Fit(signals, Options);

        var dimensions = model.Weights.Select(w => w.Dimension).Distinct().ToList();
        dimensions.Count.ShouldBeGreaterThanOrEqualTo(3);
        var totalMass = model.Weights.Sum(w => Math.Abs(w.Weight));
        totalMass.ShouldBe(1m, tolerance: 0.0001m);
    }

    [Fact]
    public void Bounding_preserves_the_sign_of_every_weight()
    {
        var signals = new List<SignalFact>();
        signals.AddRange(Many(10, _ => MultiFact(SignalKind.Saved, 2, new Dictionary<Dimension, string>
        {
            [Dimension.Technology] = "Go",
            [Dimension.Country] = "PT",
        })));
        signals.AddRange(Many(10, _ => MultiFact(SignalKind.Ignored, 2, new Dictionary<Dimension, string>
        {
            [Dimension.RemotePolicy] = "Onsite",
        })));

        var model = WeightFitter.Fit(signals, Options);

        // Saved values keep a positive weight, ignored ones negative — bounding scales, it never flips.
        model.Weights.Single(w => w.Value == "Go").Weight.ShouldBeGreaterThan(0m);
        model.Weights.Single(w => w.Value == "PT").Weight.ShouldBeGreaterThan(0m);
        model.Weights.Single(w => w.Value == "Onsite").Weight.ShouldBeLessThan(0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Over_adversarial_distributions_no_dimension_ever_exceeds_its_share(int seed)
    {
        // A family of deterministic adversarial distributions (varied by seed, no RNG): each makes one
        // dimension overwhelmingly one-sided while the rest carry thin, mixed evidence. Whatever the shape,
        // the bound must hold for every dimension that earns weight — this is AC-09 as a property.
        var dominant = new[]
        {
            Dimension.Country, Dimension.Technology, Dimension.RemotePolicy,
            Dimension.CompanySize, Dimension.SalaryBand, Dimension.EmploymentType,
        }[seed];
        var dominantShare = 30 + (seed * 13); // 30..95 ignores concentrated on the dominant dimension

        var signals = new List<SignalFact>();
        signals.AddRange(Many(dominantShare, i => MultiFact(SignalKind.Ignored, 1 + (i % 30), new Dictionary<Dimension, string>
        {
            [dominant] = "overwhelming",
            [Dimension.Technology] = i % 2 == 0 ? "Kafka" : "Rust",
            [Dimension.Country] = i % 3 == 0 ? "DE" : "NL",
        })));
        signals.AddRange(Many(6, i => MultiFact(SignalKind.Saved, 1 + (i % 20), new Dictionary<Dimension, string>
        {
            [Dimension.Technology] = "Kafka",
            [Dimension.RemotePolicy] = "Remote",
        })));

        var model = WeightFitter.Fit(signals, Options);

        foreach (var dimension in model.Weights.Select(w => w.Dimension).Distinct())
        {
            DimensionMass(model, dimension).ShouldBeLessThanOrEqualTo(Options.MaxDimensionShare + 0.0001m);
        }
    }
}
