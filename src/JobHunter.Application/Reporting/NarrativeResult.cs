using JobHunter.Domain.Reporting;

namespace JobHunter.Application.Reporting;

/// <summary>
/// The outcome of market-note synthesis (F5 T05): the text, whether it came from the model or the template,
/// and — only for a model note — the prompt version that produced it. The shape mirrors the
/// <see cref="Digest"/> constructor's invariant exactly: a <see cref="NarrativeSource.Model"/> result always
/// carries both a non-blank narrative and a prompt version, while a <see cref="NarrativeSource.Template"/>
/// result never carries a prompt version (a template made no model call, so a version would be fabricated
/// provenance). The two factories are the only way to build one, so an ill-formed pairing cannot be
/// constructed.
/// </summary>
public sealed record NarrativeResult
{
    private NarrativeResult(string narrative, NarrativeSource source, string? promptVersion)
    {
        Narrative = narrative;
        Source = source;
        PromptVersion = promptVersion;
    }

    /// <summary>The market note — always non-blank, whichever source produced it.</summary>
    public string Narrative { get; }

    public NarrativeSource Source { get; }

    /// <summary>Non-null exactly when <see cref="Source"/> is <see cref="NarrativeSource.Model"/>.</summary>
    public string? PromptVersion { get; }

    /// <summary>A note synthesised by the deep-tier model, stamped with the prompt version that produced it.</summary>
    public static NarrativeResult Model(string narrative, string promptVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(narrative);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        return new NarrativeResult(narrative.Trim(), NarrativeSource.Model, promptVersion);
    }

    /// <summary>The deterministic fallback, used when the model is unavailable or over budget (ADR-F5-0001).</summary>
    public static NarrativeResult Template(string narrative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(narrative);
        return new NarrativeResult(narrative.Trim(), NarrativeSource.Template, promptVersion: null);
    }
}
