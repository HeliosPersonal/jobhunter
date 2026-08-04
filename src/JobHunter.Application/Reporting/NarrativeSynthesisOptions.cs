namespace JobHunter.Application.Reporting;

/// <summary>
/// Tunables for market-note synthesis at digest assembly (F5 SAD §6.1, T05, ADR-F5-0001). Bound and
/// validated at startup (coding-standards §options). The narrative is optional by design — a provider
/// outage or an exhausted budget must never delay the digest — so both numbers bound the one best-effort
/// model call: <see cref="Timeout"/> caps the whole submit-and-poll budget, and <see cref="PollInterval"/>
/// spaces the status checks inside it. When the budget elapses the synthesiser falls back to the
/// deterministic template and the digest ships on time regardless.
/// </summary>
public sealed class NarrativeSynthesisOptions
{
    public const string SectionName = "NarrativeSynthesis";

    /// <summary>
    /// The whole synthesis budget — submission plus polling (ADR-F5-0001). When it elapses the model note is
    /// abandoned and the template is used, so the digest is never delayed. Deliberately short: a market note
    /// is a nicety, not the product.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How long to wait between status polls inside the budget. The default 2 s keeps polling cheap.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
}
