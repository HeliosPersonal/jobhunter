using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;

namespace JobHunter.Scrapers.Adapters;

/// <summary>
/// Resolves the one <see cref="IJobSource"/> adapter for a provider (SAD §5), implementing the Domain
/// <see cref="IJobSourceCatalog"/> port over the registered adapters. Built once from the set of adapters
/// DI provides and indexed by <see cref="IJobSource.Kind"/>, so the dispatcher never switches on a provider
/// enum and adding a provider is a new registered adapter, not a change here (QG-1). A kind with no adapter
/// resolves to <see langword="null"/> — the caller logs and skips it rather than throwing.
/// </summary>
public sealed class JobSourceCatalog : IJobSourceCatalog
{
    private readonly IReadOnlyDictionary<AtsKind, IJobSource> _adapters;

    public JobSourceCatalog(IEnumerable<IJobSource> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        var map = new Dictionary<AtsKind, IJobSource>();
        foreach (var adapter in adapters)
        {
            // A duplicate registration for a provider is a composition bug, not a runtime outcome.
            if (!map.TryAdd(adapter.Kind, adapter))
            {
                throw new InvalidOperationException(
                    $"More than one IJobSource is registered for ATS kind '{adapter.Kind}'.");
            }
        }

        _adapters = map;
    }

    /// <inheritdoc />
    public IJobSource? For(AtsKind kind) => _adapters.GetValueOrDefault(kind);
}
