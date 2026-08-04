using JobHunter.Domain.Reporting;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Reporting;

public sealed class DigestCardTests
{
    private static readonly Guid CardId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");
    private static readonly Guid DigestId = Guid.Parse("00000000-0000-0000-0000-0000000000E1");
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-0000000000D1");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");

    private static DigestCard NewCard(
        int rank = 1,
        decimal score = 82.5m,
        IReadOnlyList<string>? reasons = null,
        bool applyUrlVerified = true) =>
        new(
            CardId,
            DigestId,
            JobId,
            RunId,
            rank,
            score,
            reasons ?? ["Strong platform-engineering overlap."],
            applyUrlVerified);

    [Fact]
    public void A_valid_card_exposes_its_fields()
    {
        var card = NewCard();

        card.Id.ShouldBe(CardId);
        card.DigestId.ShouldBe(DigestId);
        card.JobId.ShouldBe(JobId);
        card.Rank.ShouldBe(1);
        card.Score.ShouldBe(82.5m);
        card.ApplyUrlVerified.ShouldBeTrue();
        card.Reasons.Count.ShouldBe(1);
    }

    [Fact]
    public void The_card_key_is_the_deterministic_key_for_its_run_and_job()
    {
        var card = NewCard();

        card.Key.ShouldBe(CardKey.For(RunId, JobId));
    }

    [Fact]
    public void An_empty_reasons_list_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewCard(reasons: []));
    }

    [Fact]
    public void A_whitespace_only_reasons_list_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewCard(reasons: ["", "  ", "\t"]));
    }

    [Fact]
    public void Blank_reasons_are_trimmed_out_but_a_real_one_survives()
    {
        var card = NewCard(reasons: ["  ", "  Strong overlap.  ", ""]);

        card.Reasons.Count.ShouldBe(1);
        card.Reasons[0].ShouldBe("Strong overlap.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_rank_below_one_is_rejected(int rank)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewCard(rank: rank));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void An_out_of_range_score_is_rejected(double score)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewCard(score: (decimal)score));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void The_score_bounds_are_inclusive(int score)
    {
        NewCard(score: score).Score.ShouldBe(score);
    }

    [Fact]
    public void A_card_may_be_unverified()
    {
        NewCard(applyUrlVerified: false).ApplyUrlVerified.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_rejects_empty_reference_ids()
    {
        Should.Throw<ArgumentException>(() => new DigestCard(CardId, Guid.Empty, JobId, RunId, 1, 50m, ["r"], true));
        Should.Throw<ArgumentException>(() => new DigestCard(CardId, DigestId, Guid.Empty, RunId, 1, 50m, ["r"], true));
        Should.Throw<ArgumentException>(() => new DigestCard(CardId, DigestId, JobId, Guid.Empty, 1, 50m, ["r"], true));
    }
}
