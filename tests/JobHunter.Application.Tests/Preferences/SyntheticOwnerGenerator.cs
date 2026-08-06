using JobHunter.Application.Preferences;
using JobHunter.Domain.Preferences;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// The nine profiles of the synthetic-behaviour corpus (F7 test-plan §The synthetic-behaviour corpus).
/// Each names a different failure mode the fitter must survive.
/// </summary>
public enum SyntheticProfile
{
    /// <summary>Clear preferences, 10% contradicting noise — baseline recovery, weights point the right way.</summary>
    ClearLowNoise,

    /// <summary>Clear preferences, 30% noise — still directionally right but with lower confidence.</summary>
    ClearHighNoise,

    /// <summary>Actions uncorrelated with every dimension — the fitter must produce no weights at all.</summary>
    Indifferent,

    /// <summary>Salary and company size move together — the combined effect must stay bounded.</summary>
    Correlated,

    /// <summary>The first half of history prefers one policy, the recent half the other — recency wins.</summary>
    ChangedMind,

    /// <summary>One country carries almost all the ignores — its weight is capped at the dimension share.</summary>
    SingleDimensionOverwhelming,

    /// <summary>Nearly everything is ignored — weights are negative, and the fit does not fall over.</summary>
    AlmostEverythingIgnored,

    /// <summary>Only fifty signals — too few to matter; the learner, not the fitter, gates activation.</summary>
    Sparse,

    /// <summary>Few card taps but several interviews and offers — outcomes dominate by their higher weight.</summary>
    OutcomeHeavy,
}

/// <summary>
/// Generates a fictional Owner's signal history with <em>planted</em> preferences, so the pure
/// <see cref="WeightFitter"/> can be asserted to recover them — or, for the indifferent profile, to recover
/// nothing (F7 ADR-F7-0001, test-plan). Every profile is driven by a seeded <see cref="Random"/> so a
/// failure reproduces from its seed, and every timestamp is relative to a passed-in reference time so the
/// generator holds no clock. This lives in the test project because it exists only to exercise the fitter.
/// </summary>
public static class SyntheticOwnerGenerator
{
    // The value pools the profiles draw from. Liked values earn a positive action when a job aligns with the
    // Owner; disliked values earn a negative one. A neutral dimension's values are drawn independently of the
    // action, so their positive rate lands at ~0.5 and the fitter must leave them unweighted.
    private static readonly IReadOnlyDictionary<Dimension, (string[] Liked, string[] Disliked)> PreferenceDimensions =
        new Dictionary<Dimension, (string[], string[])>
        {
            [Dimension.Country] = (["DE", "NL"], ["IN", "BR"]),
            [Dimension.Technology] = (["Kafka", "Rust"], ["PHP", "COBOL"]),
            [Dimension.RemotePolicy] = (["Remote"], ["Onsite"]),
            [Dimension.SalaryBand] = (["170-200k", "200k+"], ["120-150k", "150-170k"]),
            [Dimension.CompanySize] = (["SeriesC", "Public"], ["SeriesA"]),
        };

    private static readonly IReadOnlyDictionary<Dimension, string[]> NeutralDimensions =
        new Dictionary<Dimension, string[]> { [Dimension.EmploymentType] = ["FullTime", "Contract"] };

    /// <summary>
    /// Generates the signal history for <paramref name="profile"/>. <paramref name="count"/> overrides the
    /// profile's default signal count (used by the performance ceiling); <paramref name="referenceTime"/> is
    /// the "now" ages are measured back from.
    /// </summary>
    public static List<SignalFact> Generate(
        SyntheticProfile profile, int seed, DateTimeOffset referenceTime, int? count = null)
    {
        var rng = new Random(seed);
        return profile switch
        {
            SyntheticProfile.ClearLowNoise => Aligned(rng, referenceTime, count ?? 400, noise: 0.10),
            SyntheticProfile.ClearHighNoise => Aligned(rng, referenceTime, count ?? 400, noise: 0.30),
            SyntheticProfile.Sparse => Aligned(rng, referenceTime, count ?? 50, noise: 0.10),
            SyntheticProfile.Indifferent => Indifferent(rng, referenceTime, count ?? 400),
            SyntheticProfile.Correlated => Correlated(rng, referenceTime, count ?? 300, noise: 0.10),
            SyntheticProfile.ChangedMind => ChangedMind(rng, referenceTime, count ?? 400),
            SyntheticProfile.SingleDimensionOverwhelming => SingleDimension(rng, referenceTime, count ?? 120),
            SyntheticProfile.AlmostEverythingIgnored => AlmostEverythingIgnored(rng, referenceTime, count ?? 60),
            SyntheticProfile.OutcomeHeavy => OutcomeHeavy(rng, referenceTime, count ?? 6),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown synthetic profile."),
        };
    }

    // Half the jobs align with the Owner (all liked values → a positive action), half are misaligned (all
    // disliked → a negative action); a `noise` fraction of actions is flipped, because people are not
    // consistent. Neutral dimensions get an action-independent value on every job.
    private static List<SignalFact> Aligned(Random rng, DateTimeOffset reference, int count, double noise)
    {
        var signals = new List<SignalFact>(count);
        for (var i = 0; i < count; i++)
        {
            var aligned = rng.NextDouble() < 0.5;
            var facts = new Dictionary<Dimension, string>();
            foreach (var (dimension, pool) in PreferenceDimensions)
            {
                var options = aligned ? pool.Liked : pool.Disliked;
                facts[dimension] = options[rng.Next(options.Length)];
            }

            foreach (var (dimension, values) in NeutralDimensions)
            {
                facts[dimension] = values[rng.Next(values.Length)];
            }

            var positive = aligned ^ (rng.NextDouble() < noise);
            signals.Add(Signal(rng, positive ? SignalKind.Saved : SignalKind.Ignored, reference, facts));
        }

        return signals;
    }

    // Actions are perfectly uncorrelated with the facts: every job is emitted twice, same values and same
    // age, once saved and once ignored. So every value co-occurs with exactly as many positive as negative
    // actions — a rate of 0.5 by construction, the strongest form of indifference. The fitter must invent
    // nothing from it (test-plan, the profile that matters most).
    private static List<SignalFact> Indifferent(Random rng, DateTimeOffset reference, int count)
    {
        var signals = new List<SignalFact>(count);
        for (var i = 0; i < count / 2; i++)
        {
            var facts = new Dictionary<Dimension, string>();
            foreach (var (dimension, pool) in PreferenceDimensions)
            {
                var options = pool.Liked.Concat(pool.Disliked).ToArray();
                facts[dimension] = options[rng.Next(options.Length)];
            }

            var age = rng.NextDouble() * 160;
            signals.Add(Signal(rng, SignalKind.Saved, reference, facts, age));
            signals.Add(Signal(rng, SignalKind.Ignored, reference, facts, age));
        }

        return signals;
    }

    // Salary and company size move together: a high salary implies a large company, and both are liked when
    // the job aligns. The fitter must weight both yet keep the combined effect bounded (SAD §11 D2).
    private static List<SignalFact> Correlated(Random rng, DateTimeOffset reference, int count, double noise)
    {
        var signals = new List<SignalFact>(count);
        for (var i = 0; i < count; i++)
        {
            var aligned = rng.NextDouble() < 0.5;
            var facts = new Dictionary<Dimension, string>
            {
                [Dimension.SalaryBand] = aligned ? "200k+" : "120-150k",
                [Dimension.CompanySize] = aligned ? "Public" : "SeriesA",
            };
            var positive = aligned ^ (rng.NextDouble() < noise);
            signals.Add(Signal(rng, positive ? SignalKind.Saved : SignalKind.Ignored, reference, facts));
        }

        return signals;
    }

    // Old history prefers Onsite and rejects Remote; recent history is the reverse. The 60-day half-life must
    // make the recent preference win, so Remote ends positive.
    private static List<SignalFact> ChangedMind(Random rng, DateTimeOffset reference, int count)
    {
        var signals = new List<SignalFact>(count);
        var half = count / 2;
        for (var i = 0; i < count; i++)
        {
            var recent = i >= half;
            var ageDays = recent ? rng.NextDouble() * 60 : 90 + (rng.NextDouble() * 80);
            var likesRemote = recent;

            signals.Add(Signal(
                rng,
                likesRemote ? SignalKind.Saved : SignalKind.Ignored,
                reference,
                new Dictionary<Dimension, string> { [Dimension.RemotePolicy] = "Remote" },
                ageDays));
            signals.Add(Signal(
                rng,
                likesRemote ? SignalKind.Ignored : SignalKind.Saved,
                reference,
                new Dictionary<Dimension, string> { [Dimension.RemotePolicy] = "Onsite" },
                ageDays));
        }

        return signals;
    }

    // Almost all ignores share one country; a few saves elsewhere. The country weight would run away without
    // the 0.40 dimension cap.
    private static List<SignalFact> SingleDimension(Random rng, DateTimeOffset reference, int count)
    {
        var signals = new List<SignalFact>(count);
        var overwhelming = (int)(count * 0.95);
        for (var i = 0; i < count; i++)
        {
            var ignore = i < overwhelming;
            var facts = new Dictionary<Dimension, string>
            {
                [Dimension.Country] = ignore ? "IN" : "NL",
                [Dimension.Technology] = i % 2 == 0 ? "Kafka" : "Rust",
            };
            signals.Add(Signal(rng, ignore ? SignalKind.Ignored : SignalKind.Saved, reference, facts));
        }

        return signals;
    }

    // Nearly everything is ignored across a handful of values, with a sprinkling of saves.
    private static List<SignalFact> AlmostEverythingIgnored(Random rng, DateTimeOffset reference, int count)
    {
        var values = new[] { "IN", "BR", "PL" };
        var signals = new List<SignalFact>(count);
        for (var i = 0; i < count; i++)
        {
            var positive = rng.NextDouble() < 0.05;
            signals.Add(Signal(
                rng,
                positive ? SignalKind.Saved : SignalKind.Ignored,
                reference,
                new Dictionary<Dimension, string> { [Dimension.Country] = values[i % values.Length] }));
        }

        return signals;
    }

    // A handful of card taps against several consequential outcomes for the same country. The interviews and
    // offers must outweigh the more numerous ignores because their evidence weight is far higher.
    private static List<SignalFact> OutcomeHeavy(Random rng, DateTimeOffset reference, int count)
    {
        var signals = new List<SignalFact>();
        for (var i = 0; i < count; i++)
        {
            signals.Add(Signal(rng, SignalKind.Ignored, reference,
                new Dictionary<Dimension, string> { [Dimension.Country] = "NL" }));
        }

        // Fewer positive outcomes, but each carries 4-6× the evidence weight of an ignore.
        for (var i = 0; i < 4; i++)
        {
            signals.Add(Signal(rng, SignalKind.Interview, reference,
                new Dictionary<Dimension, string> { [Dimension.Country] = "NL" }));
            signals.Add(Signal(rng, SignalKind.Offer, reference,
                new Dictionary<Dimension, string> { [Dimension.Country] = "NL" }));
        }

        return signals;
    }

    private static SignalFact Signal(
        Random rng,
        SignalKind kind,
        DateTimeOffset reference,
        IReadOnlyDictionary<Dimension, string> facts,
        double? ageDays = null)
    {
        var jobFacts = JobFacts.Create(
            facts.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)[kv.Value]));
        var age = ageDays ?? (rng.NextDouble() * 160);
        return new SignalFact(
            Guid.CreateVersion7(),
            kind,
            SignalWeights.Default.WeightFor(kind),
            jobFacts,
            reference.AddDays(-age));
    }
}
