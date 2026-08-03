using JobHunter.Domain.Companies;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Resolves the one <see cref="IPostingNormalizer"/> for a provider (SAD §5), so the normalisation handler
/// dispatches by <see cref="AtsKind"/> without ever switching on a provider enum — adding a provider is a
/// new registered normaliser, not a change to the handler (QG-1, mirroring the F1 job-source catalog).
/// </summary>
public interface IPostingNormalizerCatalog
{
    /// <summary>
    /// The normaliser for <paramref name="kind"/>, or <see langword="null"/> when none is registered — an
    /// unroutable posting is recorded as a normalisation failure and skipped, never thrown.
    /// </summary>
    IPostingNormalizer? For(AtsKind kind);
}
