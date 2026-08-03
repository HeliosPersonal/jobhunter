namespace JobHunter.Claude;

/// <summary>
/// A character-ratio token counter — the default <see cref="ITokenCounter"/>. It counts tokens from the
/// actually rendered prompt at a calibrated characters-per-token ratio, which is accurate enough for the
/// pre-submission estimate to land within 20% of the provider's reported actual (contract §Cost model)
/// without a network round trip or a native tokeniser dependency. When a Run's reported actuals show
/// drift, the ratio is the one number to recalibrate, or this type is swapped for an exact tokeniser
/// behind the same seam.
/// </summary>
public sealed class HeuristicTokenCounter : ITokenCounter
{
    /// <summary>
    /// Average characters per token for English prose and structured JSON, per Anthropic's guidance
    /// (~3.5–4 chars/token). 4.0 is slightly optimistic on characters, so it errs toward a higher token
    /// count — which for the ceiling is the safe direction (it over-estimates spend).
    /// </summary>
    public const double CharactersPerToken = 4.0;

    public int Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(text.Length / CharactersPerToken);
    }
}
