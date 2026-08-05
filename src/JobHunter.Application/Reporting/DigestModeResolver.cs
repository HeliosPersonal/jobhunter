using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Reporting;

namespace JobHunter.Application.Reporting;

/// <summary>
/// Resolves the <see cref="DigestMode"/> a digest is assembled with, from the Run's state at assembly time
/// and the counts the assembler has already computed (ADR-F5-0001 — "07:00 is a hard commitment; ship
/// partial rather than late"). Pure and total: every <see cref="RunState"/> maps to exactly one header
/// shape, so there is no unclassified day. The resolved mode is snapshotted onto the <see cref="Digest"/>,
/// so delivery renders the header the run earned at 06:45 rather than re-deriving it later (SAD §4 S2).
///
/// <para>The mapping is the ADR §Decision-outcome table made executable:
/// <list type="bullet">
///   <item><description><see cref="RunState.CostAborted"/> → <see cref="DigestMode.BudgetReached"/>: the
///   ceiling stopped the run, which is its own header whatever the counts (invariant 6, AC-06).</description></item>
///   <item><description>ranking has completed — <see cref="RunState.Researching"/>,
///   <see cref="RunState.Reporting"/> or <see cref="RunState.Delivered"/> — so the scores are final: a
///   <see cref="DigestMode.Full"/> digest when there is anything to show (a card or a suppressed count worth
///   stating), otherwise <see cref="DigestMode.NothingNew"/>, the "nothing found, nothing broken" reassurance
///   (AC-05). Ranking publishes <c>RankingCompleted</c> on its exit into <see cref="RunState.Researching"/>,
///   so the happy-path assembly always sees one of these three states, never <see cref="RunState.Ranking"/>
///   itself.</description></item>
///   <item><description>every earlier state — <see cref="RunState.Created"/>, <see cref="RunState.Enriching"/>,
///   <see cref="RunState.Matching"/>, <see cref="RunState.Ranking"/> — and a hard <see cref="RunState.Failed"/>
///   → the analysis did not finish: <see cref="DigestMode.Partial"/>, which states how many are still being
///   analysed (AC-06).</description></item>
/// </list></para>
/// </summary>
public static class DigestModeResolver
{
    /// <summary>
    /// The header shape for a digest assembled against <paramref name="state"/> with
    /// <paramref name="cardCount"/> shown cards and <paramref name="suppressedCount"/> suppressed. Total over
    /// every <see cref="RunState"/> — there is no day the resolver cannot classify.
    /// </summary>
    public static DigestMode Resolve(RunState state, int cardCount, int suppressedCount) => state switch
    {
        // The cost ceiling stopping the run is its own header regardless of how much completed first.
        RunState.CostAborted => DigestMode.BudgetReached,

        // Ranking is done (RankingCompleted fires on the way into Researching), so the scores are final: a
        // full digest when there is anything to say, otherwise the reassurance day. "Anything to say" is a
        // shown card or a suppressed count the hidden line reports.
        RunState.Researching or RunState.Reporting or RunState.Delivered =>
            cardCount > 0 || suppressedCount > 0 ? DigestMode.Full : DigestMode.NothingNew,

        // Every other state — including a hard Failed — means the analysis did not finish; ship partial.
        _ => DigestMode.Partial,
    };
}
