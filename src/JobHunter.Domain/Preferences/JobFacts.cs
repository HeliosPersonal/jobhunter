using System.Collections.ObjectModel;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Preferences;

/// <summary>
/// A job's characteristics <em>at the moment the Owner reacted</em>, expressed in the same
/// <see cref="Dimension"/> vocabulary the learner fits on (F7 [[data-model]] §signals). This is the
/// load-bearing part of a <see cref="Signal"/> and the reason signals are not simply a join to
/// <c>jobs</c>: it snapshots salary band, country, company size, technologies, timezone band, remote
/// policy and employment type as they were when reacted to, so a later edit to the job cannot rewrite
/// what the Owner is recorded as having reacted to.
///
/// <para>A dimension maps to one or more values — most to a single value, <see cref="Dimension.Technology"/>
/// to several — so the fitter can enumerate the <c>(dimension, value)</c> pairs a signal supports. It is a
/// value object: two snapshots are equal by their facts. A snapshot with no facts cannot be constructed —
/// a signal without facts teaches nothing (T01 AC) — and blank values are dropped, a dimension left with
/// no value dropped with them.</para>
/// </summary>
public sealed class JobFacts : ValueObject
{
    private readonly SortedDictionary<Dimension, IReadOnlyList<string>> _facts;

    private JobFacts(SortedDictionary<Dimension, IReadOnlyList<string>> facts) => _facts = facts;

    /// <summary>The dimensions this snapshot carries a value for, in a stable order.</summary>
    public IReadOnlyCollection<Dimension> Dimensions => _facts.Keys;

    /// <summary>
    /// Builds a snapshot from a dimension-to-values map. Values are trimmed, blanks dropped and duplicates
    /// removed per dimension (order preserved); a dimension left with no value is dropped. Throws if nothing
    /// survives, because a factless signal teaches nothing.
    /// </summary>
    public static JobFacts Create(IReadOnlyDictionary<Dimension, IReadOnlyList<string>> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var cleaned = new SortedDictionary<Dimension, IReadOnlyList<string>>();
        foreach (var (dimension, values) in facts)
        {
            if (values is null)
            {
                continue;
            }

            var cleanedValues = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (cleanedValues.Count > 0)
            {
                cleaned[dimension] = new ReadOnlyCollection<string>(cleanedValues);
            }
        }

        if (cleaned.Count == 0)
        {
            throw new ArgumentException("A signal's job facts must not be empty (a factless signal teaches nothing).", nameof(facts));
        }

        return new JobFacts(cleaned);
    }

    /// <summary>The values recorded for <paramref name="dimension"/>, or an empty list when none was snapshotted.</summary>
    public IReadOnlyList<string> ValuesFor(Dimension dimension) =>
        _facts.TryGetValue(dimension, out var values) ? values : [];

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var (dimension, values) in _facts)
        {
            yield return dimension;
            foreach (var value in values)
            {
                yield return value;
            }

            // A boundary marker so {A:[x], B:[y]} and {A:[x,y]} are not equal by a flattened sequence.
            yield return null;
        }
    }
}
