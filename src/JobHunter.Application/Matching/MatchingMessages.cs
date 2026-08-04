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
