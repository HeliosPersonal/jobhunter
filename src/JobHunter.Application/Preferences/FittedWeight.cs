using JobHunter.Domain.Preferences;

namespace JobHunter.Application.Preferences;

/// <summary>
/// One weight the fitter produced for a <c>(dimension, value)</c> (F7 SAD §5): the signed
/// <see cref="Weight"/> in <c>[-1, +1]</c>, the recency-weighted <see cref="PositiveRate"/> that produced
/// it (retained so the explanation quotes a stable number), and the ids of the signals that support it —
/// always at least <see cref="PreferenceWeight.MinSupportingSignals"/> by construction of the fit (QG-1,
/// AC-03). It is the pure-layer counterpart of the persisted <see cref="PreferenceWeight"/>; the learner
/// (T05) maps one to the other.
/// </summary>
public sealed record FittedWeight(
    Dimension Dimension,
    string Value,
    decimal Weight,
    decimal PositiveRate,
    IReadOnlyList<Guid> SupportingSignalIds);
