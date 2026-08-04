using JobHunter.Domain.Pipeline;

namespace JobHunter.Application.Matching;

/// <summary>
/// A Run is in <see cref="RunState.Matching"/> with a submitted matching batch whose results are not yet
/// retrieved, so its provider batch should be polled (F4 SAD §6.1). The matching analogue of F3's
/// <c>BatchPollDue</c>: F3's poll handler is hard-wired to the enrichment stage, so matching carries its
/// own poll trigger rather than modifying an F3 file (T05 done-when: no F3 file is modified). Published by
/// the matching submit handler once the batch commits, and by the orchestrator when resuming a
/// <c>Matching</c> Run — the batch is polled, never resubmitted (AC-05). Handled by the matching poll
/// handler (T06). Keyed on <see cref="RunId"/>.
/// </summary>
public sealed record MatchingPollDue(Guid RunId);

/// <summary>
/// A matching provider batch has ended and its results are ready to stream, parse and store (F4 SAD §6.1,
/// T06). The deep-tier analogue of F3's <c>BatchResultsReady</c>: F3's message is consumed by the
/// enrichment result-processing handler, so matching carries its own hand-off rather than modifying an F3
/// file. Published by the matching poll handler the moment
/// <see cref="Domain.Abstractions.ProviderBatchState.Ended"/> is observed; handled by the matching
/// result-processing handler (T06), which streams each item, upserts a match or records a per-item failure,
/// writes the actual-cost ledger entry and advances the Run to <see cref="RunState.Ranking"/>. An internal
/// application message, not a cross-boundary event. Keyed on <see cref="RunId"/>; the unique
/// <c>(job_id, run_id, profile_id)</c> match index makes reprocessing safe (invariant 3).
/// </summary>
public sealed record MatchingResultsReady(Guid RunId, Guid BatchId, string ProviderBatchId);
