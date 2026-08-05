using JobHunter.Domain.Reporting;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Reporting;

public sealed class DigestTests
{
    private static readonly Guid DigestId = Guid.Parse("00000000-0000-0000-0000-0000000000E1");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");

    private static SuppressionTally Tally(string reason, int count) =>
        SuppressionTally.TryCreate(reason, count).Value;

    private static DigestCard Card(int rank, string job) =>
        new(
            Guid.Parse($"00000000-0000-0000-0000-0000000000C{rank}"),
            DigestId,
            Guid.Parse(job),
            RunId,
            rank,
            80m + rank,
            ["A reason."],
            applyUrlVerified: true);

    private static Digest NewDigest(
        DigestMode mode = DigestMode.Full,
        int totalNewJobs = 40,
        int strongMatches = 6,
        decimal? avgSalaryUsd = 145000m,
        int suppressedCount = 34,
        IReadOnlyList<SuppressionTally>? suppressionBreakdown = null,
        int carriedOverCount = 0,
        int companiesChecked = 0,
        int analysedCount = 0,
        IReadOnlyList<string>? degradedSources = null,
        string? narrative = "A calm market today.",
        NarrativeSource narrativeSource = NarrativeSource.Model,
        string? promptVersion = "digest-v1",
        IReadOnlyList<DigestCard>? cards = null)
    {
        var clock = new FakeClock();
        return new Digest(
            DigestId,
            RunId,
            mode,
            totalNewJobs,
            strongMatches,
            avgSalaryUsd,
            suppressedCount,
            suppressionBreakdown ?? [Tally("Below your salary floor", 30), Tally("Wrong location", 4)],
            carriedOverCount,
            companiesChecked,
            analysedCount,
            degradedSources ?? [],
            narrative,
            narrativeSource,
            promptVersion,
            cards ?? [],
            clock.UtcNow);
    }

    [Fact]
    public void A_valid_digest_exposes_its_fields()
    {
        var digest = NewDigest(mode: DigestMode.Full, companiesChecked: 42, analysedCount: 7);

        digest.Id.ShouldBe(DigestId);
        digest.RunId.ShouldBe(RunId);
        digest.Mode.ShouldBe(DigestMode.Full);
        digest.TotalNewJobs.ShouldBe(40);
        digest.StrongMatches.ShouldBe(6);
        digest.AvgSalaryUsd.ShouldBe(145000m);
        digest.SuppressedCount.ShouldBe(34);
        digest.SuppressionBreakdown.Count.ShouldBe(2);
        digest.CompaniesChecked.ShouldBe(42);
        digest.AnalysedCount.ShouldBe(7);
        digest.NarrativeSource.ShouldBe(NarrativeSource.Model);
        digest.Narrative.ShouldBe("A calm market today.");
        digest.PromptVersion.ShouldBe("digest-v1");
    }

    [Fact]
    public void The_suppressed_count_must_reconcile_to_its_breakdown()
    {
        // D7 / invariant 11: a footer that says "34 hidden" while its reasons sum to 30 is a silent-filter
        // lie, and the type forbids it.
        Should.Throw<ArgumentException>(() => NewDigest(
            suppressedCount: 34,
            suppressionBreakdown: [Tally("Below your salary floor", 30)]));
    }

    [Fact]
    public void An_empty_breakdown_reconciles_to_a_zero_suppressed_count()
    {
        var digest = NewDigest(suppressedCount: 0, suppressionBreakdown: []);

        digest.SuppressedCount.ShouldBe(0);
        digest.SuppressionBreakdown.ShouldBeEmpty();
    }

    [Fact]
    public void A_model_narrative_must_carry_text()
    {
        Should.Throw<ArgumentException>(() => NewDigest(
            narrativeSource: NarrativeSource.Model, narrative: "  "));
    }

    [Fact]
    public void A_model_narrative_must_carry_a_prompt_version()
    {
        Should.Throw<ArgumentException>(() => NewDigest(
            narrativeSource: NarrativeSource.Model, promptVersion: null));
    }

    [Fact]
    public void A_template_narrative_must_not_carry_a_prompt_version()
    {
        // A template made no model call, so a prompt version would be fabricated provenance (SAD S4).
        Should.Throw<ArgumentException>(() => NewDigest(
            narrativeSource: NarrativeSource.Template,
            narrative: "Auto-generated summary.",
            promptVersion: "digest-v1"));
    }

    [Fact]
    public void A_template_narrative_without_a_prompt_version_is_valid()
    {
        var digest = NewDigest(
            narrativeSource: NarrativeSource.Template,
            narrative: "Auto-generated summary.",
            promptVersion: null);

        digest.NarrativeSource.ShouldBe(NarrativeSource.Template);
        digest.PromptVersion.ShouldBeNull();
    }

    [Fact]
    public void An_empty_digest_with_no_narrative_is_valid_as_a_template()
    {
        // The "no Run at all" path (SAD §6.3) still ships a digest; silence is never an outcome.
        var digest = NewDigest(
            totalNewJobs: 0,
            strongMatches: 0,
            avgSalaryUsd: null,
            suppressedCount: 0,
            suppressionBreakdown: [],
            narrativeSource: NarrativeSource.Template,
            narrative: null,
            promptVersion: null);

        digest.Narrative.ShouldBeNull();
        digest.Cards.ShouldBeEmpty();
    }

    [Fact]
    public void Cards_are_ordered_by_rank()
    {
        var digest = NewDigest(cards:
        [
            Card(2, "00000000-0000-0000-0000-0000000000D2"),
            Card(1, "00000000-0000-0000-0000-0000000000D1"),
            Card(3, "00000000-0000-0000-0000-0000000000D3"),
        ]);

        digest.Cards.Select(c => c.Rank).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Duplicate_ranks_are_rejected()
    {
        Should.Throw<ArgumentException>(() => NewDigest(cards:
        [
            Card(1, "00000000-0000-0000-0000-0000000000D1"),
            Card(1, "00000000-0000-0000-0000-0000000000D2"),
        ]));
    }

    [Fact]
    public void A_card_belonging_to_another_digest_is_rejected()
    {
        var foreignCard = new DigestCard(
            Guid.Parse("00000000-0000-0000-0000-0000000000C9"),
            Guid.Parse("00000000-0000-0000-0000-00000000EEEE"),
            Guid.Parse("00000000-0000-0000-0000-0000000000D1"),
            RunId,
            1,
            81m,
            ["A reason."],
            applyUrlVerified: true);

        Should.Throw<ArgumentException>(() => NewDigest(cards: [foreignCard]));
    }

    [Fact]
    public void A_zero_average_salary_is_rejected_as_absent_instead()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewDigest(avgSalaryUsd: 0m));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void Negative_counts_are_rejected(int totalNew, int strong, int suppressed, int carriedOver)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewDigest(
            totalNewJobs: totalNew,
            strongMatches: strong,
            suppressedCount: suppressed,
            suppressionBreakdown: [],
            carriedOverCount: carriedOver));
    }

    [Fact]
    public void Constructor_rejects_an_empty_run()
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentException>(() => new Digest(
            DigestId, Guid.Empty, DigestMode.Full, 0, 0, null, 0, [], 0, 0, 0, [], null,
            NarrativeSource.Template, null, [], clock.UtcNow));
    }

    [Fact]
    public void Degraded_sources_are_trimmed_and_deblanked()
    {
        var digest = NewDigest(degradedSources: ["  Acme (greenhouse)  ", "", "  "]);

        digest.DegradedSources.ShouldBe(["Acme (greenhouse)"]);
    }

    [Fact]
    public void Carried_over_count_is_carried()
    {
        NewDigest(carriedOverCount: 12).CarriedOverCount.ShouldBe(12);
    }

    [Fact]
    public void A_negative_companies_checked_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewDigest(companiesChecked: -1));
    }

    [Fact]
    public void A_negative_analysed_count_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewDigest(analysedCount: -1));
    }
}
