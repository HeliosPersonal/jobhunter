using JobHunter.Domain.Preferences;

namespace JobHunter.Application.Applications;

/// <summary>
/// The outcome signal weights as configuration (F6 SAD §8, T08 done-when 4): how much each kind of reaction
/// counts as evidence for the preference learner. Bound and validated at startup (coding-standards §options)
/// and turned into the <see cref="SignalWeights"/> the <see cref="OutcomeSignalPublisher"/> resolves each
/// signal's weight through, so the weights are tunable without a deploy and never hand-copied literals. The
/// defaults are the SAD §8 table (card action 1.0, applied 2.0, rejected 3.0, interview 4.0, offer 6.0), so
/// an unconfigured deployment behaves exactly as <see cref="SignalWeights.Default"/>.
///
/// <para>The single <see cref="CardAction"/> weight is shared by every F5 card action; F6 mints only the four
/// outcome kinds, but the whole table is bound in one place so the two features stay in step.</para>
/// </summary>
public sealed class SignalWeightOptions
{
    public const string SectionName = "SignalWeights";

    /// <summary>The weight of any F5 card action (<c>Opened</c>/<c>Ignored</c>/<c>Saved</c>/<c>Rated</c>).</summary>
    public decimal CardAction { get; init; } = 1.0m;

    /// <summary>The weight of reaching <c>Applied</c>.</summary>
    public decimal Applied { get; init; } = 2.0m;

    /// <summary>The weight of reaching <c>Rejected</c>.</summary>
    public decimal Rejected { get; init; } = 3.0m;

    /// <summary>The weight of reaching <c>Interview</c>.</summary>
    public decimal Interview { get; init; } = 4.0m;

    /// <summary>The weight of reaching <c>Offer</c>.</summary>
    public decimal Offer { get; init; } = 6.0m;

    /// <summary>Builds the <see cref="SignalWeights"/> the publisher resolves each outcome's weight through.</summary>
    public SignalWeights ToWeights() => new(CardAction, Applied, Rejected, Interview, Offer);
}
