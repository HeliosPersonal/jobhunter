using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// The graceful placeholder for a command whose feature has not shipped — <c>/pipeline</c> before F6
/// (contract §Commands). The command is registered so it appears in <c>/help</c> and behaves predictably,
/// but it says plainly that the feature is not available yet rather than failing or pretending. It reads
/// nothing and never reaches for an LLM.
/// </summary>
internal sealed class PlaceholderCommandHandler : ICommandHandler
{
    private readonly string _feature;

    public PlaceholderCommandHandler(string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        _feature = feature;
    }

    public Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = "_" + MarkdownV2Escaper.Escape($"{_feature} is not available yet.") + "_";
        IReadOnlyList<RenderedMessage> messages = [RenderedMessage.PlainText(text)];
        return Task.FromResult(messages);
    }
}
