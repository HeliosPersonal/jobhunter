using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Renders one of the previous week's top-ten delivered cards into the "was this worth opening?" prompt the
/// weekly rating loop sends (F4 T20, D5). Defined in Domain so the Application-layer handler depends on the
/// port, not on the Telegram formatters (the arrow runs Telegram → Application): the handler decides
/// <em>which</em> cards are rated and gates the week, the renderer decides <em>how</em> the one prompt reads
/// and which callback tokens its rating buttons carry.
///
/// <para>It reads only the public identity carried on the <see cref="WeeklyTopCard"/> — it never touches the
/// CV (the CV crosses exactly one boundary, and it is not this one).</para>
/// </summary>
public interface IWeeklyRatingRenderer
{
    /// <summary>The single rating prompt for <paramref name="card"/> — its subject and its worth-opening buttons.</summary>
    RenderedMessage Render(WeeklyTopCard card);
}
