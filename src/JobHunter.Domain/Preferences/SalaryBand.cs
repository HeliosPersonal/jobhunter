using System.Globalization;
using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Preferences;

/// <summary>
/// Bands a published <see cref="SalaryRange"/> into the <see cref="Dimension.SalaryBand"/> vocabulary the
/// preference learner keys on (F7 data-model §preference_weights — e.g. <c>150-180k</c>). It is the one
/// <see cref="JobFacts"/> dimension with no source column, so the snapshot must derive it here.
///
/// <para>The rules mirror the digest's salary discipline (F5 SAD §6.1): only a USD annual figure is
/// banded, because a fabricated FX rate or a figure banded across the wrong period would be a lie the
/// evidence must never carry; the range midpoint chooses the band; and the scale is a fixed 30k-wide
/// grid labelled in thousands, so two nearby postings quantise to the same value the fitter can
/// aggregate. Anything that cannot be banded honestly — absent, non-USD, non-annual — has
/// <em>no</em> band, expressed as <c>null</c>, never a guess.</para>
/// </summary>
public static class SalaryBand
{
    // A 30k-wide grid: wide enough that adjacent postings share a band the fitter can aggregate, narrow
    // enough that the label still means something. Matches the F7 data-model example (150-180k).
    private const decimal BandWidth = 30_000m;

    /// <summary>
    /// The band label for <paramref name="salary"/>, or <c>null</c> when it cannot be banded honestly
    /// (no salary, a non-USD currency, or a non-annual period). Pure and side-effect free.
    /// </summary>
    public static string? Of(SalaryRange? salary)
    {
        if (salary is null)
        {
            return null;
        }

        // Band only what the digest itself would surface: USD, and annual so the thousands scale is true.
        if (!string.Equals(salary.Currency, "USD", StringComparison.Ordinal) || salary.Period != SalaryPeriod.Year)
        {
            return null;
        }

        var midpoint = (salary.Min + salary.Max) / 2m;
        var index = (int)Math.Floor(midpoint / BandWidth);
        return Label(index);
    }

    /// <summary>
    /// The <see cref="Dimension.SalaryBand"/> labels that sit <strong>wholly below</strong> a USD annual floor of
    /// <paramref name="floorUsd"/> — every band whose ceiling does not exceed the floor — in ascending order. This
    /// is the floor spoken in the learner's vocabulary: F10's <c>/floor</c> projects a negative stance on each of
    /// these bands so an explicit floor outranks any learned <em>positive</em> weight on a below-floor band (F4
    /// AC-05). It is defined only in the learner's USD grid, exactly as <see cref="Of"/> bands only USD, because a
    /// non-USD floor cannot honestly name a USD band; a zero or negative floor has nothing below it. Pure.
    /// </summary>
    public static IReadOnlyList<string> BandsWhollyBelow(decimal floorUsd)
    {
        var bands = new List<string>();

        // Band i spans [i·30k, (i+1)·30k); its ceiling is (i+1)·30k. A band is wholly below the floor when that
        // ceiling does not exceed it, so the whole band tops out at or beneath the floor line.
        for (var index = 0; (index + 1) * BandWidth <= floorUsd; index++)
        {
            bands.Add(Label(index));
        }

        return bands;
    }

    // The label for band index i on the fixed 30k-wide, thousands-scaled grid, e.g. i=5 -> "150-180k".
    private static string Label(int index)
    {
        var lowThousands = index * (int)(BandWidth / 1_000m);
        var highThousands = (index + 1) * (int)(BandWidth / 1_000m);
        return string.Create(CultureInfo.InvariantCulture, $"{lowThousands}-{highThousands}k");
    }
}
