using JobHunter.Domain.Companies;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Resolves the <see cref="IJobSource"/> adapter for a provider (SAD §5): the dispatcher hands a binding's
/// <see cref="AtsKind"/> to this catalog and gets the one adapter that fetches that provider. Defined in
/// Domain so <c>FetchSourceHandler</c> depends on the port rather than on <c>JobHunter.Scrapers</c> (the
/// layering rule forbids Application referencing Scrapers); the concrete catalog over the registered
/// adapters is supplied where Scrapers is composed.
/// </summary>
public interface IJobSourceCatalog
{
    /// <summary>
    /// The adapter for <paramref name="kind"/>, or <see langword="null"/> when no adapter is registered
    /// for it — an unroutable source is logged and skipped, never thrown (acquisition is a value domain).
    /// </summary>
    IJobSource? For(AtsKind kind);
}
