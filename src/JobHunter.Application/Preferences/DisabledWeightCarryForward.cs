using JobHunter.Domain.Preferences;

namespace JobHunter.Application.Preferences;

/// <summary>
/// Reconciles a fresh fit against the Owner's explicit switch-offs so a refit cannot silently relearn a
/// preference the Owner turned off (T08, AC-06 — "not relearned until its evidence doubles"). The learner runs
/// this while building the new model version's weights: for every disabled weight in the prior active model,
/// the corresponding <see cref="FittedWeight"/> is only allowed to relearn — as a fresh, enabled weight — once
/// its supporting evidence has at least <em>doubled</em>; until then the disabled weight is carried into the new
/// version verbatim (id aside) and stays off. A disabled value the fresh fit no longer produces is carried
/// forward too, rather than vanishing.
///
/// <para>The doubling baseline is the disabled weight's own <see cref="PreferenceWeight.SupportingSignalCount"/>.
/// Weights are immutable, so that count is frozen at the instant the Owner disabled it — the gate needs no extra
/// column and no separate "count at disable" record. Only the Owner's disables are preserved; an enabled prior
/// weight is ordinary evidence the fresh fit re-derives or drops on its own, so carrying it would freeze the
/// model against learning.</para>
///
/// <para>Pure and deterministic: no clock, no repository, no I/O. The learner owns persistence.</para>
/// </summary>
public static class DisabledWeightCarryForward
{
    /// <summary>
    /// Produces the weights for a new model version by merging <paramref name="fitted"/> with the disabled
    /// weights carried from <paramref name="prior"/>.
    /// </summary>
    /// <param name="prior">The prior active model, or <c>null</c> on the very first fit (nothing to carry).</param>
    /// <param name="newModelId">The id of the version being built; every emitted weight belongs to it.</param>
    /// <param name="fitted">The fresh fit's weights.</param>
    /// <param name="newId">Allocates a fresh weight id (from <c>IIdGenerator</c>); never <c>Guid.NewGuid</c> in a handler.</param>
    /// <param name="fittedAt">When this fit ran; the created-at of freshly learned weights.</param>
    public static IReadOnlyList<PreferenceWeight> Apply(
        PreferenceModel? prior,
        Guid newModelId,
        IReadOnlyList<FittedWeight> fitted,
        Func<Guid> newId,
        DateTimeOffset fittedAt)
    {
        ArgumentNullException.ThrowIfNull(fitted);
        ArgumentNullException.ThrowIfNull(newId);

        var disabled = prior is null
            ? new Dictionary<(Dimension, string), PreferenceWeight>()
            : prior.Weights
                .Where(w => w.Disabled)
                .ToDictionary(w => (w.Dimension, w.Value));

        var result = new List<PreferenceWeight>(fitted.Count + disabled.Count);
        var handled = new HashSet<(Dimension, string)>();

        foreach (var fit in fitted)
        {
            var key = (fit.Dimension, fit.Value);
            if (disabled.TryGetValue(key, out var off))
            {
                // The fresh fit re-derived a value the Owner switched off: this disabled key is now accounted
                // for, whichever branch handles it, so the carry-forward loop below must not re-add it.
                handled.Add(key);
                if (fit.SupportingSignalIds.Count < 2 * off.SupportingSignalCount)
                {
                    // Evidence has not yet doubled: carry it forward, still disabled, with its baseline count
                    // frozen so the doubling is measured from the same floor across successive refits.
                    result.Add(CarryForward(off, newModelId, newId()));
                    continue;
                }
            }

            // No override, or the evidence has doubled — learn (or relearn) it fresh and enabled.
            result.Add(new PreferenceWeight(
                newId(), newModelId, fit.Dimension, fit.Value, fit.Weight, fit.SupportingSignalIds, fit.PositiveRate, fittedAt));
        }

        // A disabled value the fresh fit no longer produces still has zero new evidence, which certainly has not
        // doubled: preserve the Owner's choice rather than letting the disabled preference silently disappear.
        foreach (var (key, off) in disabled)
        {
            if (!handled.Contains(key))
            {
                result.Add(CarryForward(off, newModelId, newId()));
            }
        }

        return result;
    }

    private static PreferenceWeight CarryForward(PreferenceWeight off, Guid newModelId, Guid id)
    {
        var carried = new PreferenceWeight(
            id, newModelId, off.Dimension, off.Value, off.Weight, off.SupportingSignalIds, off.PositiveRate, off.CreatedAt);
        carried.Disable(off.DisabledAt!.Value);
        return carried;
    }
}
