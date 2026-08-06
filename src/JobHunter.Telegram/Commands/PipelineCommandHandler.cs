using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/pipeline</c> (contract §Commands, F6 T09 done-when 3): the tracked applications grouped by status, in
/// the same scannable card layout as the digest (AC-01). It reads the pipeline view through
/// <see cref="IApplicationPipelineQuery"/> as of the injected <see cref="IClock"/> — never
/// <c>DateTime.Now</c>, so <c>daysInStage</c> is computed against the caller's clock and the read is
/// deterministic under test — and renders each application through the one shared
/// <see cref="CardFormatter"/>, so there is no second layout.
///
/// <para>Each status is announced by a bold header line, then its applications follow as cards, newest
/// activity first (the read model's own order). A closed posting is shown as a marker on the card, never as a
/// status (AC-07). Reading is all it does: no LLM (the command path is deterministic), no write, and
/// <strong>no CV</strong> — the pipeline view carries nothing about the Owner (the CV crosses exactly one
/// boundary, and it is not this one). An empty pipeline is answered with a single plain line, so the Owner
/// always gets a reply.</para>
/// </summary>
internal sealed class PipelineCommandHandler(
    IApplicationPipelineQuery pipeline, IClock clock, ILogger<PipelineCommandHandler> logger) : ICommandHandler
{
    private readonly IApplicationPipelineQuery _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<PipelineCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var view = await _pipeline.PipelineAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (view.Groups.Count == 0)
        {
            _logger.LogDebug("/pipeline requested but no applications are being tracked.");
            return [RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("Your pipeline is empty — no applications yet.") + "_")];
        }

        var messages = new List<RenderedMessage>();
        foreach (var group in view.Groups)
        {
            // One bold status header, then its applications as cards — the same card the digest renders.
            messages.Add(RenderedMessage.PlainText("*" + MarkdownV2Escaper.Escape(group.Status.ToString()) + "*"));
            messages.AddRange(group.Applications.Select(entry =>
                RenderedMessage.PlainText(CardFormatter.Format(ToCard(entry)))));
        }

        return messages;
    }

    private static CardView ToCard(PipelineEntry entry) => new(
        entry.Title,
        // A closed posting is a marker on the card (AC-07), never a status — it rides in the company line so
        // it is visible without inventing a keyboard the pipeline view does not act on.
        entry.PostingClosed ? entry.Company + " (posting closed)" : entry.Company,
        Stage: null,
        Location: string.Empty,
        Salary: null,
        entry.Score,
        Reasons: []);
}
