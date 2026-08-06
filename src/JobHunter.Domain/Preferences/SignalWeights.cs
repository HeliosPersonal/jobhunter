using JobHunter.Domain.Common;

namespace JobHunter.Domain.Preferences;

/// <summary>
/// The evidence weight each <see cref="SignalKind"/> contributes to a fit (F7 SAD §8): a card action
/// counts 1.0, <see cref="SignalKind.Applied"/> 2.0, <see cref="SignalKind.Rejected"/> 3.0,
/// <see cref="SignalKind.Interview"/> 4.0 and <see cref="SignalKind.Offer"/> 6.0. An outcome the Owner
/// lived through outweighs a glance at a card, so the fitter is driven mostly by consequential actions.
///
/// <para>These are <em>configuration</em> — documented, tunable without a deploy, never model-controlled
/// (SAD §8) — exposed here as a value object with a <see cref="Default"/> matching the SAD table. Two
/// weight sets are equal by their five values; a weight that is not strictly positive cannot be
/// constructed, because a non-positive evidence weight would silently erase the action it records.</para>
/// </summary>
public sealed class SignalWeights : ValueObject
{
    public SignalWeights(decimal cardAction, decimal applied, decimal rejected, decimal interview, decimal offer)
    {
        EnsurePositive(cardAction, nameof(cardAction));
        EnsurePositive(applied, nameof(applied));
        EnsurePositive(rejected, nameof(rejected));
        EnsurePositive(interview, nameof(interview));
        EnsurePositive(offer, nameof(offer));

        CardAction = cardAction;
        Applied = applied;
        Rejected = rejected;
        Interview = interview;
        Offer = offer;
    }

    /// <summary>The SAD §8 defaults: card action 1.0, applied 2.0, rejected 3.0, interview 4.0, offer 6.0.</summary>
    public static SignalWeights Default { get; } = new(1.0m, 2.0m, 3.0m, 4.0m, 6.0m);

    /// <summary>The weight shared by every card action (<c>Opened</c>, <c>Ignored</c>, <c>Saved</c>, <c>Rated</c>).</summary>
    public decimal CardAction { get; }

    public decimal Applied { get; }

    public decimal Rejected { get; }

    public decimal Interview { get; }

    public decimal Offer { get; }

    /// <summary>
    /// The evidence weight of <paramref name="kind"/> under this configuration. The four card actions all
    /// resolve to <see cref="CardAction"/>; the outcome kinds each resolve to their own weight.
    /// </summary>
    public decimal WeightFor(SignalKind kind) => kind switch
    {
        SignalKind.Opened or SignalKind.Ignored or SignalKind.Saved or SignalKind.Rated => CardAction,
        SignalKind.Applied => Applied,
        SignalKind.Rejected => Rejected,
        SignalKind.Interview => Interview,
        SignalKind.Offer => Offer,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown signal kind."),
    };

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CardAction;
        yield return Applied;
        yield return Rejected;
        yield return Interview;
        yield return Offer;
    }

    private static void EnsurePositive(decimal weight, string name)
    {
        if (weight <= 0m)
        {
            throw new ArgumentOutOfRangeException(name, weight, "A signal weight must be strictly positive.");
        }
    }
}
