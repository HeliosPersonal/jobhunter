using System.Collections.ObjectModel;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Preferences;

/// <summary>
/// One fitted version of the Owner's learned preferences (F7 [[data-model]] §preference_models,
/// [[adr/0002-evidence-threshold-and-explainability|ADR-F7-0002]]). It bundles the
/// <see cref="PreferenceWeight"/>s a refit produced, the <see cref="SignalCount"/> of evidence behind
/// them, and its monotonic <see cref="Version"/>. F4 ranking uses exactly one active model at a time.
///
/// <para>Immutable, and activation is a <em>separate</em> operation (T01 AC): a refit inserts a new
/// version and <see cref="Activate"/> flips it on atomically, so a bad refit is a rollback to the previous
/// version rather than an incident (SAD §4 S6). Activation carries the ADR's floor as a guard — a model
/// fitted on fewer than <see cref="ActivationThreshold"/> signals cannot be activated, because two weeks of
/// evidence is the point below which a single bad day dominates. When a model is not activated the reason
/// is recorded in <see cref="Notes"/> (<c>insufficient evidence: 143 signals</c>), so the absence of
/// learning is visible rather than mysterious.</para>
/// </summary>
public sealed class PreferenceModel : Entity
{
    /// <summary>Roughly two weeks of normal use — the evidence floor before any model is activated (ADR-F7-0002).</summary>
    public const int ActivationThreshold = 200;

    private readonly List<PreferenceWeight> _weights = [];

    public PreferenceModel(
        Guid id,
        int version,
        int signalCount,
        IReadOnlyList<PreferenceWeight> weights,
        DateTimeOffset fittedAt,
        string? notes = null)
        : base(id)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "A model version must be positive.");
        }

        if (signalCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(signalCount), signalCount, "A signal count cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(weights);

        if (weights.Any(w => w is null))
        {
            throw new ArgumentException("A model's weights must not contain a null.", nameof(weights));
        }

        if (weights.Any(w => w.ModelId != id))
        {
            throw new ArgumentException("Every weight in a model must reference that model.", nameof(weights));
        }

        _weights = weights.ToList();
        Version = version;
        SignalCount = signalCount;
        FittedAt = fittedAt;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private PreferenceModel()
    {
    }

    /// <summary>Monotonic across refits (unique in the store); higher is newer.</summary>
    public int Version { get; private set; }

    /// <summary>Exactly one model is active at a time (a partial unique index enforces it in the store).</summary>
    public bool IsActive { get; private set; }

    /// <summary>How much evidence produced this model — the count checked against <see cref="ActivationThreshold"/>.</summary>
    public int SignalCount { get; private set; }

    public DateTimeOffset FittedAt { get; private set; }

    /// <summary>When this model was activated; null until <see cref="Activate"/> is called.</summary>
    public DateTimeOffset? ActivatedAt { get; private set; }

    /// <summary>Why a model was not activated, e.g. <c>insufficient evidence: 143 signals</c>; null otherwise.</summary>
    public string? Notes { get; private set; }

    /// <summary>The weights this refit produced; may be empty — an indifferent Owner earns no weights.</summary>
    public IReadOnlyList<PreferenceWeight> Weights => new ReadOnlyCollection<PreferenceWeight>(_weights);

    /// <summary>True when there is enough evidence for this model to be activated (ADR-F7-0002).</summary>
    public bool HasSufficientEvidence => SignalCount >= ActivationThreshold;

    /// <summary>
    /// Turns this model on at <paramref name="activatedAt"/> (T01 AC: activation is a separate operation).
    /// Guards the ADR floor — a model fitted on fewer than <see cref="ActivationThreshold"/> signals cannot
    /// be activated. Deactivating the previously active model is the caller's atomic responsibility (SAD
    /// §4 S6); this method only turns one on.
    /// </summary>
    public void Activate(DateTimeOffset activatedAt)
    {
        if (IsActive)
        {
            return;
        }

        if (!HasSufficientEvidence)
        {
            throw new InvalidOperationException(
                $"A preference model needs at least {ActivationThreshold} signals to activate "
                + $"(has {SignalCount}).");
        }

        IsActive = true;
        ActivatedAt = activatedAt;
    }

    /// <summary>
    /// Turns this model off (SAD §4 S6): a refit deactivates the previous active version before activating
    /// the new one, so the flip is atomic and the old version stays queryable for rollback. Idempotent.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
        ActivatedAt = null;
    }
}
