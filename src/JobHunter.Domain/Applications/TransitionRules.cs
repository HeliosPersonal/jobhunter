using System.Collections.Frozen;

namespace JobHunter.Domain.Applications;

/// <summary>
/// The permitted <c>(from, to)</c> status transitions as a <b>table</b>, not a chain of conditionals
/// (SAD §5). A table can be enumerated by a test, which is how all 49 pairs — the full
/// <c>ApplicationStatus × ApplicationStatus</c> product, including the diagonal — are covered against
/// [[../contracts/application-api]] rather than the handful someone thought of.
///
/// <para>Transitions are permissive: only genuinely impossible sequences are refused, and every refusal
/// names a remedy ([[adr/0001-permissive-transitions-with-history|ADR-F6-0001]]). The diagonal is always
/// a legal idempotent no-op; the <c>→ New</c> column is refused for every source but <c>New</c> itself,
/// because there is no move back to "not yet acted on".</para>
/// </summary>
public static class TransitionRules
{
    private static readonly FrozenSet<(ApplicationStatus From, ApplicationStatus To)> Allowed = BuildAllowed();

    /// <summary>
    /// Whether <paramref name="from"/> may move to <paramref name="to"/>. A permitted transition returns
    /// <see cref="TransitionResult.Permitted"/>; a refused one returns
    /// <see cref="TransitionResult.Refused"/> carrying the remedy for that specific pair.
    /// </summary>
    public static TransitionResult Evaluate(ApplicationStatus from, ApplicationStatus to) =>
        Allowed.Contains((from, to))
            ? TransitionResult.Permitted()
            : TransitionResult.Refused(RemedyFor(from, to));

    private static FrozenSet<(ApplicationStatus, ApplicationStatus)> BuildAllowed()
    {
        const ApplicationStatus n = ApplicationStatus.New;
        const ApplicationStatus s = ApplicationStatus.Saved;
        const ApplicationStatus a = ApplicationStatus.Applied;
        const ApplicationStatus i = ApplicationStatus.Interview;
        const ApplicationStatus r = ApplicationStatus.Rejected;
        const ApplicationStatus o = ApplicationStatus.Offer;
        const ApplicationStatus g = ApplicationStatus.Ignored;

        return new (ApplicationStatus, ApplicationStatus)[]
        {
            // New — the lazily-created entry state; forward to any stage but Offer.
            (n, n), (n, s), (n, a), (n, i), (n, r), (n, g),
            // Saved — commit, advance, or drop; not straight to Offer.
            (s, s), (s, a), (s, i), (s, r), (s, g),
            // Applied — advance, decline, or correct a mis-tap back to Saved.
            (a, s), (a, a), (a, i), (a, r), (a, o), (a, g),
            // Interview — further rounds, an outcome, or dismissal; no going backwards through the funnel.
            (i, i), (i, r), (i, o), (i, g),
            // Rejected — a role can re-open (Applied), or be dropped.
            (r, a), (r, r), (r, g),
            // Offer — accepted (stays Offer) or declined (Rejected); never ignored.
            (o, r), (o, o),
            // Ignored — pulled back into the pipeline, or left ignored.
            (g, s), (g, a), (g, g),
        }.ToFrozenSet();
    }

    private static string RemedyFor(ApplicationStatus from, ApplicationStatus to)
    {
        // The most specific messages first; then the two structural refusals (→ New, and backwards through
        // the funnel), then a general fallback. Every branch names what to do instead.
        if (to == ApplicationStatus.New)
        {
            return $"An application cannot return to New once it has been acted on. Move it forward to the stage that matches reality (from {from}).";
        }

        if (from == ApplicationStatus.Offer && to == ApplicationStatus.Ignored)
        {
            return "An offer is not something you ignore; it is accepted or declined. Use Rejected to record a declined offer.";
        }

        if (from == ApplicationStatus.Rejected && to == ApplicationStatus.Interview)
        {
            return "An application cannot return to Interview after Rejected. Create a new application if the company re-opened the conversation.";
        }

        if (from == ApplicationStatus.Interview &&
            (to == ApplicationStatus.Applied || to == ApplicationStatus.Saved))
        {
            return $"Going backwards from Interview to {to} is not a real event; it is a mis-tap. Applied → Interview already exists to fix the reverse.";
        }

        if (from == ApplicationStatus.Offer &&
            (to == ApplicationStatus.Applied || to == ApplicationStatus.Interview || to == ApplicationStatus.Saved))
        {
            return $"Going backwards from Offer to {to} is not a real event. An offer is accepted (stays Offer) or declined (Rejected).";
        }

        return $"A {from} → {to} transition is not possible. Move the application to the stage that matches what actually happened.";
    }
}
