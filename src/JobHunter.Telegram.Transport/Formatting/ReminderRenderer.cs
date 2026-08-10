using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Notifications;

namespace JobHunter.Telegram.Formatting;

/// <summary>
/// The production <see cref="IReminderRenderer"/> (F6 T06): turns one due application into the single nudge
/// the reminder sweep sends the Owner. The message names the application and a <em>suggested action</em>,
/// never just a fact (done-when 2). A still-open stale application gets a "chase this" nudge with an
/// open-posting button; a closed posting gets a "drop it or apply elsewhere" nudge and <em>no</em> button,
/// because the posting is gone and re-opening it is pointless.
///
/// <para>Every dynamic value — the title and the company — passes through <see cref="MarkdownV2Escaper"/>,
/// the one escape path, so a title full of MarkdownV2 specials cannot silently fail the send. The renderer
/// reads only the public job facts on the <see cref="DueReminder"/>; the CV crosses exactly one boundary,
/// and it is not this one.</para>
/// </summary>
internal sealed class ReminderRenderer : IReminderRenderer
{
    /// <summary>Titles longer than this are truncated at a word boundary, as the digest cards are.</summary>
    private const int MaxTitleGraphemes = 80;

    public RenderedMessage Render(DueReminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);

        var title = MarkdownV2Escaper.Escape(MarkdownV2Escaper.Truncate(reminder.Title, MaxTitleGraphemes));
        var company = MarkdownV2Escaper.Escape(reminder.Company);
        // The markup is adjacent constants and the values are already escaped (rule 9): never interpolate a
        // raw value next to active markup, or one unescaped special silently fails the whole send.
        var subject = "*" + title + "* at " + company;

        if (reminder.PostingClosed)
        {
            // The posting is gone: there is nothing to chase and no button to open. Suggest closing it out.
            var closed = string.Join("\n", [
                "⏰ " + subject,
                MarkdownV2Escaper.Escape(
                    "The posting has closed — drop this one or apply elsewhere, then update its status."),
            ]);
            return RenderedMessage.PlainText(closed);
        }

        // Still open: name the stage it is sitting in and nudge the Owner to chase it, with an open button.
        var stage = MarkdownV2Escaper.Escape(SuggestedAction(reminder.Status));
        var text = string.Join("\n", ["⏰ " + subject, stage]);
        IReadOnlyList<IReadOnlyList<InlineButton>> keyboard =
            [[InlineButton.ForUrl("Open posting", reminder.ApplyUrl)]];
        return new RenderedMessage(text, keyboard);
    }

    /// <summary>The chase line for a still-open application, phrased as an action for the stage it is in.</summary>
    private static string SuggestedAction(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Saved => "Saved a while ago — apply now or drop it.",
        ApplicationStatus.Applied => "Applied a while ago with no word back — follow up or move on.",
        ApplicationStatus.Interview => "Interviewing — chase the next step or a decision.",
        // Only the statuses with a reminder threshold ever reach here; anything else still gets a nudge.
        _ => $"Waiting in {status} — check where it stands.",
    };
}
