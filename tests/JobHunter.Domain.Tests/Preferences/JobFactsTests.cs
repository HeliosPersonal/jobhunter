using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Preferences;

/// <summary>
/// T01: the job-facts snapshot. It carries the job's characteristics as they were when the Owner reacted,
/// in the same <see cref="Dimension"/> vocabulary the learner fits on, and it must be non-empty — a
/// factless signal teaches nothing.
/// </summary>
public sealed class JobFactsTests
{
    [Fact]
    public void A_snapshot_exposes_the_values_recorded_per_dimension()
    {
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Country] = ["DE"],
            [Dimension.Technology] = ["Kafka", "Go"],
        });

        facts.ValuesFor(Dimension.Country).ShouldBe(["DE"]);
        facts.ValuesFor(Dimension.Technology).ShouldBe(["Kafka", "Go"]);
        facts.Dimensions.ShouldBe([Dimension.Country, Dimension.Technology], ignoreOrder: true);
    }

    [Fact]
    public void An_absent_dimension_reads_back_as_empty()
    {
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Country] = ["DE"],
        });

        facts.ValuesFor(Dimension.RemotePolicy).ShouldBeEmpty();
    }

    [Fact]
    public void Blank_values_are_dropped_and_a_dimension_left_empty_is_removed()
    {
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Country] = ["  DE  "],
            [Dimension.Technology] = ["   ", ""],
        });

        // The country is trimmed; the all-blank technologies dimension vanishes entirely.
        facts.ValuesFor(Dimension.Country).ShouldBe(["DE"]);
        facts.Dimensions.ShouldBe([Dimension.Country]);
    }

    [Fact]
    public void Duplicate_values_within_a_dimension_are_collapsed()
    {
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Technology] = ["Kafka", "Kafka", "Go"],
        });

        facts.ValuesFor(Dimension.Technology).ShouldBe(["Kafka", "Go"]);
    }

    [Fact]
    public void A_snapshot_with_no_surviving_facts_cannot_be_constructed()
    {
        // A signal without facts teaches nothing (T01 AC), so an all-blank map is rejected.
        Should.Throw<ArgumentException>(() => JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Country] = ["   "],
        }));
    }

    [Fact]
    public void An_empty_map_cannot_be_constructed()
    {
        Should.Throw<ArgumentException>(
            () => JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>()));
    }

    [Fact]
    public void Two_snapshots_with_the_same_facts_are_equal()
    {
        var a = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Country] = ["DE"],
            [Dimension.Technology] = ["Kafka", "Go"],
        });
        var b = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Technology] = ["Kafka", "Go"],
            [Dimension.Country] = ["DE"],
        });

        a.ShouldBe(b);
    }

    [Fact]
    public void Facts_split_across_dimensions_are_not_equal_to_the_same_values_merged()
    {
        var split = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Country] = ["x"],
            [Dimension.Technology] = ["y"],
        });
        var merged = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Country] = ["x", "y"],
        });

        split.ShouldNotBe(merged);
    }
}
