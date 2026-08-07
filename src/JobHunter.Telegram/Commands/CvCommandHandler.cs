using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/cv</c> (catalogue §Profile · State read): the status of the active CV — its version, when that version
/// was activated, and how many current matches were computed against it. It reads the metadata-only
/// <see cref="ICvStatusQuery"/>, so there is <strong>no path for CV content to reach the reply</strong>: the CV
/// crosses exactly one boundary (the F4 match prompt) and it is not this one, which is why the F4 leakage scan
/// can leave this path uncovered by construction rather than by an allowlist.
///
/// <para>Read-only: it never uploads a CV — that path is F4's, outside the command surface. With no active CV
/// it says so plainly rather than rendering a zero; a version uploaded but not yet activated is reported
/// honestly, never with a fabricated activation date. Every value reaches the reply through the one MarkdownV2
/// escaper.</para>
/// </summary>
internal sealed class CvCommandHandler(
    ICvStatusQuery status, ILogger<CvCommandHandler> logger) : ICommandHandler
{
    private readonly ICvStatusQuery _status = status ?? throw new ArgumentNullException(nameof(status));
    private readonly ILogger<CvCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = await _status.ActiveAsync(cancellationToken).ConfigureAwait(false);
        if (status is null)
        {
            _logger.LogDebug("/cv requested but no CV is active.");
            return [Plain("You have no active CV yet. Upload one so I can match roles against it.")];
        }

        var matches = status.MatchCount == 1 ? "1 role" : $"{status.MatchCount} roles";
        var activation = status.ActivatedAt is { } activatedAt
            ? $", active since {activatedAt:d MMM yyyy}"
            : " (uploaded, not yet activated)";

        return [Plain($"CV v{status.Version}{activation}. Matched against {matches}.")];
    }

    // A single plain line, escaped, so a value with MarkdownV2 punctuation always renders literally.
    private static RenderedMessage Plain(string text) =>
        RenderedMessage.PlainText(MarkdownV2Escaper.Escape(text));
}
