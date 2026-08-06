namespace JobHunter.Application.Preferences;

/// <summary>
/// The bounding-and-normalisation stage of the fit (F7 SAD §8, [[adr/0001-transparent-frequency-weighting|
/// ADR-F7-0001]]): an overwhelming one-sided pattern in a single dimension must never become the only thing
/// that matters (AC-09, QG-3). It treats each dimension's total absolute weight as its <em>contribution
/// mass</em> to the preference component, normalises those masses across dimensions, and caps any one
/// dimension at <c>maxDimensionShare</c> of the total — redistributing the surplus over the dimensions that
/// have room (classic water-filling).
///
/// <para>The cap is the hard invariant and always holds. The sum of masses reaches one only when it
/// <em>can</em> — with a cap of 0.40 that needs at least three dimensions carrying weight (2 × 0.40 &lt; 1);
/// with one or two, every dimension is pinned to the cap and the total is simply less than one. Within a
/// dimension the final mass is split across its values in proportion to their raw magnitude, and each
/// value's sign is preserved — bounding scales influence, it never flips a preference.</para>
/// </summary>
internal static class DimensionBounding
{
    /// <summary>
    /// Bounds and normalises the raw fitted <paramref name="weights"/> so that no single dimension's total
    /// absolute weight exceeds <paramref name="maxDimensionShare"/>. Returns rescaled weights; the
    /// <see cref="FittedWeight.PositiveRate"/> and supporting signal ids of each are carried through unchanged.
    /// </summary>
    public static IReadOnlyList<FittedWeight> Apply(
        IReadOnlyList<FittedWeight> weights, decimal maxDimensionShare)
    {
        if (weights.Count == 0)
        {
            return weights;
        }

        // Raw contribution mass per dimension, and the grand total.
        var rawMass = new Dictionary<Domain.Preferences.Dimension, decimal>();
        foreach (var weight in weights)
        {
            rawMass[weight.Dimension] = rawMass.GetValueOrDefault(weight.Dimension) + Math.Abs(weight.Weight);
        }

        var total = rawMass.Values.Sum();
        if (total <= 0m)
        {
            return weights;
        }

        var normalisedMass = WaterFill(
            rawMass.ToDictionary(kv => kv.Key, kv => kv.Value / total),
            maxDimensionShare);

        // Split each dimension's final mass across its values in proportion to their raw magnitude,
        // preserving sign. A dimension whose values all decayed to zero magnitude keeps a zero share.
        var result = new List<FittedWeight>(weights.Count);
        foreach (var weight in weights)
        {
            var dimensionRaw = rawMass[weight.Dimension];
            var scaled = dimensionRaw <= 0m
                ? 0m
                : normalisedMass[weight.Dimension] * (Math.Abs(weight.Weight) / dimensionRaw)
                    * Math.Sign(weight.Weight);
            result.Add(weight with { Weight = scaled });
        }

        return result;
    }

    /// <summary>
    /// Redistributes normalised dimension shares so none exceeds <paramref name="cap"/>: any over-cap
    /// dimension is pinned to the cap and its surplus spread proportionally over the dimensions that still
    /// have headroom, repeating until stable. Converges because each round pins at least one more dimension
    /// (or runs out of free mass, the fewer-than-three-dimensions case).
    /// </summary>
    private static Dictionary<Domain.Preferences.Dimension, decimal> WaterFill(
        Dictionary<Domain.Preferences.Dimension, decimal> shares, decimal cap)
    {
        var pinned = new HashSet<Domain.Preferences.Dimension>();
        while (true)
        {
            var over = shares.Where(kv => !pinned.Contains(kv.Key) && kv.Value > cap).Select(kv => kv.Key).ToList();
            if (over.Count == 0)
            {
                break;
            }

            decimal surplus = 0m;
            foreach (var dimension in over)
            {
                surplus += shares[dimension] - cap;
                shares[dimension] = cap;
                pinned.Add(dimension);
            }

            var freeMass = shares.Where(kv => !pinned.Contains(kv.Key)).Sum(kv => kv.Value);
            if (freeMass <= 0m)
            {
                // Every dimension is pinned to the cap — there is nowhere to put the surplus. This is the
                // one- or two-dimension case, where the total mass is deliberately below one.
                break;
            }

            foreach (var dimension in shares.Keys.Where(k => !pinned.Contains(k)).ToList())
            {
                shares[dimension] += surplus * (shares[dimension] / freeMass);
            }
        }

        return shares;
    }
}
