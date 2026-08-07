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
    // The callback action token behind a transition button: "st:{target}:{applicationId}" (catalogue
    // §/pipeline). The target status names the move and the application id names its subject; the id is an
    // internal identifier, never a fact and never the CV (invariant 12). The live route to F6's
    // ChangeApplicationStatus path is wired with the rest of the callback registry (T10) — this task renders
    // the buttons the matrix permits, the tap becomes an event next.
    private const string TransitionToken = "st";

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
            // One bold status header, then its applications as cards — the same card the digest renders, each
            // carrying the buttons for the moves the F6 matrix permits from this status (AC-03).
            messages.Add(RenderedMessage.PlainText("*" + MarkdownV2Escaper.Escape(group.Status.ToString()) + "*"));
            messages.AddRange(group.Applications.Select(entry =>
                new RenderedMessage(CardFormatter.Format(ToCard(entry)), TransitionKeyboard(group.Status, entry.Id))));
        }

        return messages;
    }

    // The legal next transitions from a status as one row of inline buttons (AC-03, catalogue §/pipeline). The
    // matrix owns which moves are real — the idempotent no-op is not among them — so advancing is one tap. Each
    // button carries "st:{target}:{applicationId}"; the id names the subject, never a fact (invariant 12), and
    // the live route to F6's ChangeApplicationStatus path is wired with the callback registry (T10).
    private static IReadOnlyList<IReadOnlyList<InlineButton>> TransitionKeyboard(ApplicationStatus from, Guid applicationId)
    {
        var buttons = TransitionRules.NextTransitions(from)
            .Select(to => new InlineButton(to.ToString(), $"{TransitionToken}:{to}:{applicationId}"))
            .ToArray();

        return buttons.Length == 0 ? [] : [buttons];
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
