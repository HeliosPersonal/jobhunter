using JobHunter.Domain.Applications;
using JobHunter.Domain.Notifications;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Renders one due application into the nudge the reminder sweep sends (F6 SAD §6.2, T06). Defined in Domain
/// so the Application-layer sweep handler depends on the port, not on the Telegram formatters (the arrow runs
/// Telegram → Application): the sweep decides <em>which</em> applications are reminded and suppresses repeats,
/// the renderer decides <em>how</em> the one message reads.
///
/// <para>The message names the application and a suggested action, never just a fact (done-when 2): a still-open
/// stale application gets a "chase this" nudge with an open-posting button; a closed posting gets a "drop it or
/// apply elsewhere" nudge. The renderer reads only the public job facts carried on the <see cref="DueReminder"/>;
/// it never touches the CV (invariant: the CV crosses exactly one boundary, and it is not this one).</para>
/// </summary>
public interface IReminderRenderer
{
    /// <summary>The single message for <paramref name="reminder"/> — its subject and its suggested action.</summary>
    RenderedMessage Render(DueReminder reminder);
}
