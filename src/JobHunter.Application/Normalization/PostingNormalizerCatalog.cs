using JobHunter.Domain.Companies;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Indexes the registered <see cref="IPostingNormalizer"/> implementations by <see cref="AtsKind"/>
/// (SAD §5). Built once from the set DI provides, so the normalisation handler resolves a normaliser by
/// provider without switching on an enum, and a duplicate registration is a composition bug caught at
/// construction rather than a silent last-wins.
/// </summary>
public sealed class PostingNormalizerCatalog : IPostingNormalizerCatalog
{
    private readonly IReadOnlyDictionary<AtsKind, IPostingNormalizer> _normalizers;

    public PostingNormalizerCatalog(IEnumerable<IPostingNormalizer> normalizers)
    {
        ArgumentNullException.ThrowIfNull(normalizers);

        var map = new Dictionary<AtsKind, IPostingNormalizer>();
        foreach (var normalizer in normalizers)
        {
            if (!map.TryAdd(normalizer.Kind, normalizer))
            {
                throw new InvalidOperationException(
                    $"More than one IPostingNormalizer is registered for ATS kind '{normalizer.Kind}'.");
            }
        }

        _normalizers = map;
    }

    /// <inheritdoc />
    public IPostingNormalizer? For(AtsKind kind) => _normalizers.GetValueOrDefault(kind);
}
