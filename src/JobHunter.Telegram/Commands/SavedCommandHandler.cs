using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/saved</c> (contract §Commands): the roles the Owner saved from a digest card, newest-first, each in
/// the same scannable card layout as the morning digest (AC-12). It reads the store through
/// <see cref="ISavedRolesQuery"/> — a <c>Saved</c>-kind signal joined back to the job, its company, its
/// latest score and its current match — and maps each <see cref="SavedRole"/> onto the one shared
/// <see cref="CardFormatter"/>, so there is no second layout.
///
/// <para>Reading is all it does: no LLM, no CV, no write. An empty history is answered with a single plain
/// line rather than an empty message, so the Owner always gets a reply.</para>
/// </summary>
internal sealed class SavedCommandHandler(
    ISavedRolesQuery saved, ILogger<SavedCommandHandler> logger) : ICommandHandler
{
    /// <summary>How many saved roles a single <c>/saved</c> shows — a bounded page, never the whole history.</summary>
    private const int PageSize = 10;

    private readonly ISavedRolesQuery _saved = saved ?? throw new ArgumentNullException(nameof(saved));
    private readonly ILogger<SavedCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<Domain.Notifications.RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var roles = await _saved.SavedAsync(PageSize, cancellationToken).ConfigureAwait(false);
        if (roles.Count == 0)
        {
            _logger.LogDebug("/saved requested but the Owner has no saved roles.");
            return [Domain.Notifications.RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("You have not saved any roles yet.") + "_")];
        }

        return [.. roles.Select(role =>
            Domain.Notifications.RenderedMessage.PlainText(CardFormatter.Format(ToCard(role))))];
    }

    private static CardView ToCard(SavedRole role) => new(
        role.Title,
        role.Company,
        role.Stage,
        role.Countries.Count > 0 ? string.Join(" / ", role.Countries) : role.RemotePolicy,
        ToSalary(role),
        role.Score,
        role.Reasons);

    private static CardSalary? ToSalary(SavedRole role) =>
        role.SalaryMin is { } min && role.SalaryMax is { } max
            ? new CardSalary(min, max, role.SalaryCurrency, IsEstimate: false, Confidence: null)
            : null;
}
