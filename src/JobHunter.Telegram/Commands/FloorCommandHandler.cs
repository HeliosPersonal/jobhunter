using System.Globalization;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/floor &lt;amount&gt; [currency]</c> (contract §Profile, F10 T08): sets the Owner's explicit salary floor on
/// the Profile. Explicit beats learned (F4 AC-05), so this overrides whatever F7 inferred. The change is
/// <strong>previewed before it is made</strong>: the reply states how many of today's shown jobs the floor would
/// have affected — counted by <see cref="ISalaryFloorPreviewQuery"/> against exactly the suppression rule
/// (same-currency, high-confidence, wholly below) — then a short-lived per-chat <see cref="ConversationState"/>
/// is stored awaiting the Owner's confirmation. Nothing is written in this step.
///
/// <para>Forgiving by construction: no amount lists the usage rather than erroring; a malformed or non-positive
/// amount, or a currency that is not a three-letter ISO code, is a business outcome with a usage line, never an
/// exception. The currency defaults to EUR and is upper-cased. The pending state carries the parsed amount and
/// currency as structured values — never free-text the Owner typed — so the confirm step can write them through
/// <see cref="JobHunter.Domain.Profiles.Profile.SetSalaryFloor"/> and <see cref="IProfileRepository"/>. No LLM,
/// no CV: the CV crosses exactly one boundary, and it is not this one.</para>
///
/// <para>The <em>resume</em> half of the flow completes the command: the <see cref="ConversationCoordinator"/>
/// resolves the Owner's <c>confirm</c> reply to a resume and hands it to <see cref="ResumeAsync"/>, which loads the
/// one active Profile, applies the parsed floor through <see cref="JobHunter.Domain.Profiles.Profile.SetSalaryFloor"/>
/// and commits it — the single write path for an explicit floor. Anything other than a confirmation neither writes
/// nor clears the pending state, so the Owner can still confirm or <c>/cancel</c>; a confirmation is always terminal
/// and clears the state, even when there is no active Profile or the stored context cannot be read back.</para>
/// </summary>
internal sealed class FloorCommandHandler(
    ISalaryFloorPreviewQuery preview,
    IConversationStateStore state,
    IProfileRepository profiles,
    IClock clock,
    ILogger<FloorCommandHandler> logger) : IResumableCommandHandler
{
    /// <summary>The registry name a pending state carries, so the resume step (T10) knows which command to resume.</summary>
    private const string CommandName = "floor";

    /// <summary>The step the multi-step flow waits for — the Owner's confirmation of the previewed change.</summary>
    private const string AwaitingConfirm = "confirm";

    /// <summary>The context keys the parsed floor rides under — structured values, never typed content.</summary>
    private const string AmountKey = "amount";
    private const string CurrencyKey = "currency";

    /// <summary>The currency used when the Owner names an amount but no currency (catalogue §Profile).</summary>
    private const string DefaultCurrency = "EUR";

    /// <summary>The reply that applies the previewed floor; anything else leaves the state pending (SAD §6.2).</summary>
    private const string ConfirmWord = "confirm";

    private readonly ISalaryFloorPreviewQuery _preview = preview ?? throw new ArgumentNullException(nameof(preview));
    private readonly IConversationStateStore _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly IProfileRepository _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<FloorCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tokens = (request.Arguments ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            // No amount: list what the command needs rather than erroring. Nothing is previewed or stored.
            return [Usage()];
        }

        if (!decimal.TryParse(tokens[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0m)
        {
            // A malformed or non-positive amount is a business outcome, not an exception: say what a floor needs.
            _logger.LogDebug("/floor rejected an unparseable or non-positive amount.");
            return [Usage()];
        }

        var currency = tokens.Length > 1 ? tokens[1].ToUpperInvariant() : DefaultCurrency;
        if (!IsIsoCurrency(currency))
        {
            // Not a three-letter ISO code: the same forgiving usage line, no preview, no state.
            _logger.LogDebug("/floor rejected a currency that is not a three-letter ISO code.");
            return [Usage()];
        }

        // Preview the change before making it: how many of today's shown jobs this floor would have suppressed,
        // by exactly the suppression rule. The write itself is deferred to the confirm step (T10).
        var affected = await _preview.CountAffectedAsync(amount, currency, cancellationToken).ConfigureAwait(false);

        var pending = new ConversationState(
            CommandName,
            AwaitingConfirm,
            new Dictionary<string, string>
            {
                [AmountKey] = amount.ToString(CultureInfo.InvariantCulture),
                [CurrencyKey] = currency,
            },
            _clock.UtcNow);
        await _state.SetAsync(request.ChatId, pending, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "/floor previewed a {Currency} floor affecting {Affected} of today's shown job(s).", currency, affected);

        var affectedPhrase = affected == 0
            ? "none of today's shown jobs"
            : $"{affected} of today's shown jobs";
        var amountText = amount.ToString(CultureInfo.InvariantCulture);

        return [RenderedMessage.PlainText(MarkdownV2Escaper.Escape(
            $"A floor of {amountText} {currency} would have affected {affectedPhrase}. "
            + "Reply confirm to apply it, or /cancel to stop."))];
    }

    public async Task<IReadOnlyList<RenderedMessage>> ResumeAsync(
        CommandResumeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Anything but a confirmation is not terminal: neither write nor clear, so the Owner can still confirm or
        // /cancel. Re-prompt with the same wording rather than treating the reply as its own command (AC-08).
        if (!string.Equals(request.Input.Trim(), ConfirmWord, StringComparison.OrdinalIgnoreCase))
        {
            return [RenderedMessage.PlainText(MarkdownV2Escaper.Escape(
                "Reply confirm to apply the floor, or /cancel to stop."))];
        }

        // A confirmation is terminal either way — clear the pending state first so a malformed context or a
        // missing Profile never wedges the chat on a floor that can never be applied.
        await _state.ClearAsync(request.ChatId, cancellationToken).ConfigureAwait(false);

        if (!request.Context.TryGetValue(AmountKey, out var rawAmount)
            || !decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || !request.Context.TryGetValue(CurrencyKey, out var currency)
            || !IsIsoCurrency(currency))
        {
            _logger.LogWarning("/floor resumed with an unreadable pending context; no floor was applied.");
            return [RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("That floor could not be applied — start again with /floor.") + "_")];
        }

        var profile = await _profiles.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            _logger.LogWarning("/floor resumed with no active profile; no floor was applied.");
            return [RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("There is no active profile to set a floor on.") + "_")];
        }

        // The one write path for an explicit floor: apply the previewed value and commit. Explicit beats learned
        // (F4 AC-05). The amount and currency came from the parsed preview, never free-text the Owner typed.
        profile.SetSalaryFloor(amount, currency, _clock.UtcNow);
        await _profiles.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("/floor applied a {Currency} salary floor.", currency);

        var amountText = amount.ToString(CultureInfo.InvariantCulture);
        return [RenderedMessage.PlainText(MarkdownV2Escaper.Escape(
            $"Your salary floor is now {amountText} {currency}."))];
    }

    // The forgiving usage line, shown whenever the amount or currency cannot be read. Italicised, escaped once.
    private static RenderedMessage Usage() => RenderedMessage.PlainText(
        "_" + MarkdownV2Escaper.Escape($"Usage: /floor <amount> [currency] — e.g. /floor 120000 USD (default {DefaultCurrency}).") + "_");

    // A three-letter A–Z ISO 4217 code, the same shape the Profile mutator enforces on the write side.
    private static bool IsIsoCurrency(string currency)
    {
        if (currency.Length != 3)
        {
            return false;
        }

        foreach (var c in currency)
        {
            if (c is < 'A' or > 'Z')
            {
                return false;
            }
        }

        return true;
    }
}
