namespace JobHunter.Claude;

/// <summary>
/// Counts the tokens in a piece of already-rendered prompt text. It is a seam so the estimator measures
/// the <em>real</em> input rather than a per-job heuristic — that is what makes the cost estimate land
/// within 20% of the provider's reported actual (contract §Cost model). A more exact tokeniser can be
/// substituted here without touching <see cref="CostAccountant"/>.
/// </summary>
public interface ITokenCounter
{
    /// <summary>The token count of <paramref name="text"/>. Empty or whitespace text is zero tokens.</summary>
    int Count(string text);
}
