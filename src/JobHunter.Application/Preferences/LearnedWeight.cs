using JobHunter.Domain.Preferences;

namespace JobHunter.Application.Preferences;

/// <summary>
/// One learned <see cref="PreferenceWeight"/> as the Owner sees it before deciding whether to switch it off
/// (T08 C6, AC-03/AC-06): the id needed to disable it, the <c>(dimension, value)</c> it pulls on, its signed
/// pull, whether it is already disabled, and the one plain sentence <see cref="WeightExplanation"/> renders.
/// It is the shared shape both the API weights endpoint and the Telegram override command project, so the two
/// surfaces quote the same explanation and address the same id.
///
/// <para>It carries only the learned facts and their explanation — nothing about the Owner's CV, which crosses
/// exactly one boundary and not this one (F4 invariant).</para>
/// </summary>
/// <param name="WeightId">The weight's id — what a disable request addresses.</param>
/// <param name="Dimension">The dimension the weight pulls on.</param>
/// <param name="Value">The value within that dimension.</param>
/// <param name="Weight">The signed pull in <c>[-1, +1]</c> on the preference component.</param>
/// <param name="Disabled">True when the Owner has switched it off; still listed, because it stays inspectable.</param>
/// <param name="Explanation">The one-sentence, evidence-quoting explanation (AC-03).</param>
public sealed record LearnedWeight(
    Guid WeightId,
    Dimension Dimension,
    string Value,
    decimal Weight,
    bool Disabled,
    string Explanation);
