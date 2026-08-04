using JobHunter.Application.Reporting;
using JobHunter.Domain.Reporting;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Reporting;

/// <summary>
/// T03: the pure grouping of a Run's suppressed candidates into the footer breakdown (F5 SAD §6.1, AC-07,
/// invariant 11). The properties that carry it: shown candidates are ignored; the counts reconcile to the
/// number suppressed; a reason-less suppressed candidate is folded under one explicit "Unspecified" bucket
/// rather than dropped (losing it from the count is the exact failure the breakdown prevents); and tallies
/// are ordered by descending count, then reason, so the largest leads and the order is deterministic.
/// </summary>
public sealed class SuppressionSummarizerTests
{
    private static DigestCandidate Shown(decimal score = 90m) =>
        new(Guid.CreateVersion7(), score, Suppressed: false, SuppressionReason: null, ["A fit"], SalaryUsd: null,
            "https://apply.example.com/shown");

    private static DigestCandidate Suppressed(string? reason) =>
        new(Guid.CreateVersion7(), 20m, Suppressed: true, reason, ["Below the bar"], SalaryUsd: null,
            "https://apply.example.com/suppressed");

    [Fact]
    public void It_ignores_shown_candidates()
    {
        var breakdown = SuppressionSummarizer.Summarize([Shown(), Shown()]);

        breakdown.ShouldBeEmpty();
    }

    [Fact]
    public void It_groups_by_reason_and_the_counts_reconcile_to_the_suppressed_total()
    {
        var breakdown = SuppressionSummarizer.Summarize(
        [
            Shown(),
            Suppressed("Below presentation threshold"),
            Suppressed("Below presentation threshold"),
            Suppressed("Off-target role family"),
        ]);

        breakdown.Sum(t => t.Count).ShouldBe(3);
        breakdown[0].Reason.ShouldBe("Below presentation threshold");
        breakdown[0].Count.ShouldBe(2);
        breakdown[1].Reason.ShouldBe("Off-target role family");
        breakdown[1].Count.ShouldBe(1);
    }

    [Fact]
    public void A_reasonless_suppressed_candidate_falls_under_the_unspecified_bucket_not_dropped()
    {
        var breakdown = SuppressionSummarizer.Summarize([Suppressed(null), Suppressed("   ")]);

        // Both fold into one bucket, and the count still reconciles: nothing suppressed is ever lost.
        var tally = breakdown.ShouldHaveSingleItem();
        tally.Reason.ShouldBe(SuppressionSummarizer.UnspecifiedReason);
        tally.Count.ShouldBe(2);
    }

    [Fact]
    public void It_trims_the_reason_so_padded_duplicates_group_together()
    {
        var breakdown = SuppressionSummarizer.Summarize(
            [Suppressed("Off-target"), Suppressed("  Off-target  ")]);

        breakdown.ShouldHaveSingleItem().Count.ShouldBe(2);
    }

    [Fact]
    public void Equal_counts_break_the_tie_by_reason_ordinally()
    {
        var breakdown = SuppressionSummarizer.Summarize(
            [Suppressed("Zeta"), Suppressed("Alpha")]);

        // Same count, so the ordinal reason decides — a stable, deterministic footer order.
        breakdown.Select(t => t.Reason).ShouldBe(["Alpha", "Zeta"]);
    }
}
