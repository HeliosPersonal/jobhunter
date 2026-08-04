using JobHunter.Domain.Reporting;

namespace JobHunter.Application.Reporting;

/// <summary>
/// Produces the digest's market note (F5 T05, ADR-F5-0001). The one implementation makes a bounded
/// best-effort deep-tier call and falls back to a deterministic template; the assembler depends on this
/// seam so its own tests stay a pure unit and a substitute can stand in for the network. Whatever happens —
/// a ceiling breach, a provider outage, a slow batch, a dead day — this returns a <see cref="NarrativeResult"/>
/// and <strong>never throws and never blocks past its budget</strong>: the narrative is optional, and a
/// nicety must never delay or fail the digest.
/// </summary>
public interface INarrativeSynthesizer
{
    /// <summary>
    /// Synthesises the market note for <paramref name="input"/> within the Run's remaining ceiling and the
    /// configured time budget. On a successful model call returns a <see cref="NarrativeSource.Model"/>
    /// result stamped with the prompt version; on anything else — nothing to say, a ceiling breach (the
    /// client is never called), an unavailable provider, or an exhausted budget — returns a
    /// <see cref="NarrativeSource.Template"/> result so the digest still ships.
    /// </summary>
    Task<NarrativeResult> SynthesizeAsync(
        Guid runId,
        NarrativeInput input,
        CancellationToken cancellationToken = default);
}
