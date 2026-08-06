using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Search;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// Bridges the real F9 <c>/search</c> handler into the T11 command router. <c>/search</c> is registered, not
/// a placeholder (F9 shipped it), so its handler produces genuine results through the shared
/// <see cref="Domain.Abstractions.ISearchQuery"/> port — the one search path (decision O12). The existing
/// <see cref="SearchCommandHandler"/> returns rendered text; this adapter passes the command arguments
/// through and wraps that text as a single message, so nothing about the search behaviour is reimplemented.
/// </summary>
internal sealed class SearchCommandAdapter : ICommandHandler
{
    private readonly SearchCommandHandler _inner;

    public SearchCommandAdapter(SearchCommandHandler inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rendered = await _inner.HandleAsync(request.Arguments, cancellationToken).ConfigureAwait(false);
        return [RenderedMessage.PlainText(rendered)];
    }
}
