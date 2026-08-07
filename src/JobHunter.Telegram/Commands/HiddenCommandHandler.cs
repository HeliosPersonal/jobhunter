using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/hidden</c> (contract §Digest and discovery, F7 T08 done-when 6): what the latest Run suppressed,
/// grouped by the reason it was withheld, each job in the same scannable card layout as the digest. This is
/// [[CONTEXT]] invariant 11 made interactive — the digest footer gives the count, this gives the jobs — so a
/// wrong learned weight is caught the moment the Owner recognises a role they wanted among the hidden ones.
/// F7 owns this handler; F10 only registers it against its catalogue (the ownership table), so there is one
/// implementation of "show what was hidden".
///
/// <para>It reads the store through <see cref="IHiddenJobsQuery"/> — the latest Run's suppressed jobs,
/// best-score first, each with its non-blank reason (invariant 11) — and renders each through the one shared
/// <see cref="CardFormatter"/>, so there is no second layout. Reading is all it does: no LLM, no write, and
/// <strong>no CV</strong> — the hidden view carries nothing about the Owner (the CV crosses exactly one
/// boundary, and it is not this one). An empty result is answered with a single plain line, so the Owner
/// always gets a reply.</para>
/// </summary>
internal sealed class HiddenCommandHandler(
    IHiddenJobsQuery hidden, ILogger<HiddenCommandHandler> logger) : ICommandHandler
{
    /// <summary>How many hidden jobs a single <c>/hidden</c> shows — a bounded page, never the whole day.</summary>
    private const int PageSize = 25;

    private readonly IHiddenJobsQuery _hidden = hidden ?? throw new ArgumentNullException(nameof(hidden));
    private readonly ILogger<HiddenCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jobs = await _hidden.HiddenAsync(PageSize, cancellationToken).ConfigureAwait(false);
        if (jobs.Count == 0)
        {
            _logger.LogDebug("/hidden requested but the latest run suppressed nothing.");
            return [RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("Nothing was hidden today.") + "_")];
        }

        // Group by reason in first-seen order, so the reason that withheld the most jobs the Owner is most
        // likely to question is a header they can scan, then its jobs as cards below it (catalogue layout).
        var messages = new List<RenderedMessage>();
        foreach (var group in GroupByReason(jobs))
        {
            messages.Add(RenderedMessage.PlainText(
                "*" + MarkdownV2Escaper.Escape($"{group.Reason} — {group.Jobs.Count}") + "*"));
            messages.AddRange(group.Jobs.Select(job =>
                RenderedMessage.PlainText(CardFormatter.Format(ToCard(job)))));
        }

        return messages;
    }

    private static IEnumerable<(string Reason, List<HiddenJob> Jobs)> GroupByReason(IReadOnlyList<HiddenJob> jobs)
    {
        var order = new List<string>();
        var byReason = new Dictionary<string, List<HiddenJob>>(StringComparer.Ordinal);
        foreach (var job in jobs)
        {
            if (!byReason.TryGetValue(job.SuppressionReason, out var bucket))
            {
                bucket = [];
                byReason[job.SuppressionReason] = bucket;
                order.Add(job.SuppressionReason);
            }

            bucket.Add(job);
        }

        return order.Select(reason => (reason, byReason[reason]));
    }

    // The suppression reason rides as the card's single reason line, so the "why" travels with the job the
    // Owner is deciding whether the model was wrong to hide.
    private static CardView ToCard(HiddenJob job) => new(
        job.Title, job.Company, Stage: null, Location: string.Empty, Salary: null, job.Score,
        Reasons: [job.SuppressionReason]);
}
