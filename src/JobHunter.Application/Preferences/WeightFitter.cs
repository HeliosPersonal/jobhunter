using JobHunter.Domain.Preferences;

namespace JobHunter.Application.Preferences;

/// <summary>
/// The pure fitting function at the heart of F7 ([[adr/0001-transparent-frequency-weighting|ADR-F7-0001]],
/// SAD §5): recorded reactions become bounded, explainable weights, with no clock and no repository in its
/// signature, so a fictional Owner with known preferences can be simulated and the fitter asserted to
/// recover them — including the case where it must recover <em>nothing</em> from noise.
///
/// <para>The method, in one paragraph: keep the signals inside the <see cref="FittingOptions.Window"/>;
/// group them by the <c>(dimension, value)</c> pairs their <see cref="JobFacts"/> carry; per group compute a
/// recency-weighted positive rate — each signal's stored evidence weight scaled by an exponential
/// <see cref="FittingOptions.RecencyHalfLife"/> decay, positives over the total; drop a value below the
/// three-signal evidence floor (AC-03) or inside the indifference deadband (the indifferent Owner earns
/// nothing); map the surviving rate to a signed weight in <c>[-1, +1]</c>; then bound and normalise so no
/// one dimension dominates (SAD §8, AC-09). Every surviving weight cites the ids of the signals that
/// produced it (QG-1).</para>
/// </summary>
public static class WeightFitter
{
    /// <summary>
    /// Fits preference weights from <paramref name="signals"/>, decaying each by recency from
    /// <see cref="FittingOptions.ReferenceTime"/> and dropping any value with fewer than
    /// <see cref="PreferenceWeight.MinSupportingSignals"/> supporting signals.
    /// </summary>
    public static FittedModel Fit(IReadOnlyList<SignalFact> signals, FittingOptions options)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(options);

        var oldestAllowed = options.ReferenceTime - options.Window;
        var inWindow = signals.Where(s => s.OccurredAt > oldestAllowed).ToList();

        // Accumulate, per (dimension, value), the recency-weighted positive and total mass and the distinct
        // supporting signal ids. A value earns a weight only if it clears the evidence floor and sits outside
        // the indifference band; direction and magnitude then come from its recency-weighted positive rate.
        var accumulators = new Dictionary<(Dimension Dimension, string Value), ValueAccumulator>();
        foreach (var signal in inWindow)
        {
            var decayed = signal.Weight * RecencyFactor(signal.OccurredAt, options);
            var contribution = IsPositive(signal.Kind) ? decayed : 0m;

            foreach (var dimension in signal.Facts.Dimensions)
            {
                foreach (var value in signal.Facts.ValuesFor(dimension))
                {
                    var key = (dimension, value);
                    if (!accumulators.TryGetValue(key, out var accumulator))
                    {
                        accumulator = new ValueAccumulator();
                        accumulators[key] = accumulator;
                    }

                    accumulator.Add(signal.SignalId, decayed, contribution);
                }
            }
        }

        var fitted = new List<FittedWeight>();
        foreach (var (key, accumulator) in accumulators)
        {
            if (accumulator.DistinctSignalCount < PreferenceWeight.MinSupportingSignals)
            {
                // A rate without three signals behind it is a coincidence, not a preference (AC-03).
                continue;
            }

            if (accumulator.TotalMass <= 0m)
            {
                // Every supporting signal decayed to nothing — no usable evidence.
                continue;
            }

            var positiveRate = accumulator.PositiveMass / accumulator.TotalMass;
            if (Math.Abs(positiveRate - 0.5m) <= options.IndifferenceBand)
            {
                // Reacted to evenly — the Owner is indifferent to this value, so it earns no weight.
                continue;
            }

            fitted.Add(new FittedWeight(
                key.Dimension,
                key.Value,
                RateToWeight(positiveRate),
                positiveRate,
                accumulator.SignalIds));
        }

        var bounded = DimensionBounding.Apply(fitted, options.MaxDimensionShare);
        return new FittedModel(bounded, inWindow.Count);
    }

    /// <summary>
    /// The exponential recency multiplier for a signal at <paramref name="occurredAt"/>: <c>0.5 ^ (age /
    /// halfLife)</c>, so a signal one half-life old counts half as much as one at the reference time (SAD §8).
    /// A future-dated signal (age &lt; 0) is clamped to a factor of 1 — it cannot count for more than a
    /// present one.
    /// </summary>
    private static decimal RecencyFactor(DateTimeOffset occurredAt, FittingOptions options)
    {
        var age = options.ReferenceTime - occurredAt;
        if (age <= TimeSpan.Zero)
        {
            return 1m;
        }

        var halfLives = age.TotalDays / options.RecencyHalfLife.TotalDays;
        return (decimal)Math.Pow(0.5, halfLives);
    }

    /// <summary>Maps a recency-weighted positive rate in <c>[0, 1]</c> to a signed weight in <c>[-1, +1]</c>.</summary>
    private static decimal RateToWeight(decimal positiveRate) => (2m * positiveRate) - 1m;

    /// <summary>
    /// A signal's polarity (ADR-F7-0001): saving, opening, rating, applying, interviewing and being offered
    /// are positive engagement; ignoring and rejecting are negative. <c>Opened</c> and <c>Rated</c> are the
    /// two engagement kinds the ADR's prose omits — both are the Owner leaning in, so both count positive.
    /// </summary>
    private static bool IsPositive(SignalKind kind) => kind switch
    {
        SignalKind.Ignored or SignalKind.Rejected => false,
        _ => true,
    };

    /// <summary>Per-value running totals: recency-weighted positive and total mass, and the distinct signal ids.</summary>
    private sealed class ValueAccumulator
    {
        private readonly List<Guid> _signalIds = [];
        private readonly HashSet<Guid> _seen = [];

        public decimal PositiveMass { get; private set; }

        public decimal TotalMass { get; private set; }

        public int DistinctSignalCount => _signalIds.Count;

        public IReadOnlyList<Guid> SignalIds => _signalIds;

        public void Add(Guid signalId, decimal decayedWeight, decimal positiveContribution)
        {
            TotalMass += decayedWeight;
            PositiveMass += positiveContribution;
            if (_seen.Add(signalId))
            {
                _signalIds.Add(signalId);
            }
        }
    }
}
