using JobHunter.Application.Reporting;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Reporting;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Reporting;

/// <summary>
/// T09: the pure classification behind every degraded-day header (ADR-F5-0001 table). The resolver maps the
/// Run's state at 06:45 — plus the counts the assembler has already computed — onto one of the four
/// <see cref="DigestMode"/> shapes, and the delivery layer renders from the <em>stored</em> mode so a
/// re-render tomorrow cannot silently re-classify a delivered digest (SAD §4 S2). Every mapping is a data
/// point here, one row per ADR case, so a change to the table is a change to this test.
/// </summary>
public sealed class DigestModeResolverTests
{
    [Theory]
    // Ranking has completed (RankingCompleted fires into Researching) and there are cards — the normal morning.
    [InlineData(RunState.Researching, 5, 0, DigestMode.Full)]
    [InlineData(RunState.Reporting, 5, 0, DigestMode.Full)]
    [InlineData(RunState.Delivered, 9, 34, DigestMode.Full)]
    // A finished run with everything suppressed but nothing shown still has something to say (the hidden line).
    [InlineData(RunState.Researching, 0, 12, DigestMode.Full)]
    [InlineData(RunState.Reporting, 0, 12, DigestMode.Full)]
    // A finished run that genuinely found nothing — zero shown and zero suppressed — is the reassurance day.
    [InlineData(RunState.Researching, 0, 0, DigestMode.NothingNew)]
    [InlineData(RunState.Reporting, 0, 0, DigestMode.NothingNew)]
    [InlineData(RunState.Delivered, 0, 0, DigestMode.NothingNew)]
    // Any state before ranking completes means the analysis has not finished — a partial day.
    [InlineData(RunState.Created, 0, 0, DigestMode.Partial)]
    [InlineData(RunState.Enriching, 3, 1, DigestMode.Partial)]
    [InlineData(RunState.Matching, 3, 1, DigestMode.Partial)]
    [InlineData(RunState.Ranking, 3, 1, DigestMode.Partial)]
    // A hard failure is incomplete work, presented as partial rather than pretending it finished.
    [InlineData(RunState.Failed, 0, 0, DigestMode.Partial)]
    // The cost ceiling stopping the run is its own header, whatever the counts.
    [InlineData(RunState.CostAborted, 4, 2, DigestMode.BudgetReached)]
    [InlineData(RunState.CostAborted, 0, 0, DigestMode.BudgetReached)]
    public void It_maps_run_state_and_counts_to_the_header_shape(
        RunState state, int cardCount, int suppressedCount, DigestMode expected) =>
        DigestModeResolver.Resolve(state, cardCount, suppressedCount).ShouldBe(expected);
}
