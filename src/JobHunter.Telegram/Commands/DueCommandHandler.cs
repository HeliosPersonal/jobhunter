using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/due</c> (contract §Commands, F6 SAD §6.2): the applications past their stage threshold, each with a
/// suggested action — the <em>pull</em> version of the 08:00 reminder sweep (the push). It reads the same due
/// set the sweep does through <see cref="IDueReminderQuery"/> as of the injected <see cref="IClock"/> — never
/// <c>DateTime.Now</c>, so due-ness is measured against the caller's clock and the read is deterministic under
/// test — and renders each through the one shared <see cref="IReminderRenderer"/>, so a pulled nudge reads
/// exactly like a pushed one and there is no second layout.
///
/// <para>Unlike the sweep, the pull does <strong>not</strong> suppress an already-reminded application: the
/// Owner asked to see everything outstanding, so the QG-3 one-per-condition rule (which stops the push from
/// re-nudging) does not apply here. Reading is all it does: no LLM, no write, and <strong>no CV</strong> — the
/// due view carries nothing about the Owner (the CV crosses exactly one boundary, and it is not this one). An
/// empty result is answered with a single plain line, so the Owner always gets a reply.</para>
/// </summary>
internal sealed class DueCommandHandler(
    IDueReminderQuery due, IReminderRenderer renderer, IClock clock, ILogger<DueCommandHandler> logger)
    : ICommandHandler
{
    private readonly IDueReminderQuery _due = due ?? throw new ArgumentNullException(nameof(due));
    private readonly IReminderRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<DueCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reminders = await _due.DueAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (reminders.Count == 0)
        {
            _logger.LogDebug("/due requested but nothing is past its stage threshold.");
            return [RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("Nothing is due — your pipeline is up to date.") + "_")];
        }

        // Each due application through the sweep's own renderer, so the pull reads like the push (done-when 2).
        return [.. reminders.Select(_renderer.Render)];
    }
}
