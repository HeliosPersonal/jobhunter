using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Preferences;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/prefs</c> (catalogue §Profile · State read, F10 T08): the learned preferences the Owner reads before
/// deciding to switch any off. Each active weight is rendered as the one plain sentence
/// <see cref="WeightExplanation"/> produces through the shared <see cref="ActiveWeightsQuery"/>, quoting the
/// count and total of the reaction that earned it (AC-03) — so this surface and the API weights endpoint quote
/// identical text. Strongest pull first, disabled weights still listed and flagged, because a weight the Owner
/// can read is a weight they can question and switch off.
///
/// <para>Below the <see cref="PreferenceModel.ActivationThreshold"/> signals learning needs before it shapes a
/// ranking, it says how many more are needed rather than rendering nothing, so the absence of learning is
/// legible rather than mysterious; the count comes from the metadata-only <see cref="IPreferenceStatusQuery"/>,
/// which still answers when no model is active. Read-only, no LLM, and <strong>no CV</strong> — the CV crosses
/// exactly one boundary and it is not this one. Every value reaches the reply through the one MarkdownV2
/// escaper.</para>
/// </summary>
internal sealed class PrefsCommandHandler(
    ActiveWeightsQuery weights,
    IPreferenceStatusQuery status,
    ILogger<PrefsCommandHandler> logger) : ICommandHandler
{
    private readonly ActiveWeightsQuery _weights = weights ?? throw new ArgumentNullException(nameof(weights));
    private readonly IPreferenceStatusQuery _status = status ?? throw new ArgumentNullException(nameof(status));
    private readonly ILogger<PrefsCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = await _status.LatestAsync(cancellationToken).ConfigureAwait(false);

        // No active model yet: learning has not turned on, so state how many more signals it needs rather than
        // rendering an empty list. A never-fitted store (null) leaves the whole threshold outstanding.
        if (status is null || !status.HasActiveModel)
        {
            var have = status?.SignalCount ?? 0;
            var needed = Math.Max(0, PreferenceModel.ActivationThreshold - have);
            _logger.LogDebug("/prefs requested below the evidence floor ({Have} signals).", have);
            return [Plain(
                $"I'm still learning your preferences: {have} of {PreferenceModel.ActivationThreshold} signals so far, "
                + $"{needed} more before I start shaping the ranking.")];
        }

        var learned = await _weights.ActiveAsync(cancellationToken).ConfigureAwait(false);
        if (learned.Count == 0)
        {
            // Enough evidence, but an evenly-reacted Owner earns no weights — say so rather than nothing (F7 QG).
            _logger.LogDebug("/prefs requested with an active model that learned no strong preferences.");
            return [Plain("I have enough signals, but haven't found any strong preferences yet — you react fairly evenly across roles.")];
        }

        // One sentence per weight, disabled ones flagged so a switched-off preference stays inspectable.
        return [.. learned.Select(w => Plain(w.Disabled ? $"{w.Explanation} (switched off)" : w.Explanation))];
    }

    // A single plain line, escaped, so a sentence with MarkdownV2 punctuation always renders literally.
    private static RenderedMessage Plain(string text) =>
        RenderedMessage.PlainText(MarkdownV2Escaper.Escape(text));
}
