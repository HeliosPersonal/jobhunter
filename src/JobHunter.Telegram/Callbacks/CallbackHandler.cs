using JobHunter.Application.Actions;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Transport;

namespace JobHunter.Telegram.Callbacks;

/// <summary>
/// Turns an inline-keyboard tap into a recorded action and the acknowledgement the Owner sees (F5 T10,
/// AC-03/AC-08/AC-09, QG-3, [[../contracts/telegram-messages|contract]] §Callback payloads). It parses the
/// <c>{action}:{shortId}</c> payload, HMAC-resolves the short id among the recently delivered cards through
/// <see cref="ICardResolutionQuery"/> and <see cref="CallbackDataCodec"/>, delegates the action-apply and
/// signal-capture <em>transaction</em> to the Application <see cref="RecordCardActionHandler"/> (keeping the
/// arch arrow Telegram → Application), then acknowledges the query and rewrites the tapped card's keyboard
/// exactly as the contract fixes it.
///
/// <para>Every failure the Owner could hit is a clear message, never a silent no-op (AC-09): a payload that
/// will not parse, a short id that no longer resolves, a callback with no message to edit, or a job that has
/// closed all produce the plain "This role has closed" acknowledgement and record nothing invalid. The
/// resolution window is caller-owned through <see cref="IClock"/>, so a stale tap from before it falls out
/// of scope and gets that same message rather than resolving against an unbounded history. The bot secret
/// lives inside the codec and never reaches here; no CV is anywhere near this path (invariant 12).</para>
/// </summary>
internal sealed class CallbackHandler
{
    // The plain acknowledgement for every unresolvable tap — a bad payload, a stale id, a closed job. It is a
    // clear message, not a crash and not a silent no-op (AC-09).
    private const string ClosedMessage = "This role has closed";

    private readonly ICardResolutionQuery _cards;
    private readonly CallbackDataCodec _codec;
    private readonly RecordCardActionHandler _action;
    private readonly ICallbackResponder _responder;
    private readonly IClock _clock;
    private readonly TimeSpan _window;
    private readonly ILogger<CallbackHandler> _logger;

    public CallbackHandler(
        ICardResolutionQuery cards,
        CallbackDataCodec codec,
        RecordCardActionHandler action,
        ICallbackResponder responder,
        IClock clock,
        TimeSpan resolutionWindow,
        ILogger<CallbackHandler> logger)
    {
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _responder = responder ?? throw new ArgumentNullException(nameof(responder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _window = resolutionWindow;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task HandleAsync(TelegramCallbackQuery callback, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);

        // A payload that will not parse, or a callback with no message to edit, cannot resolve to a card —
        // acknowledge plainly and stop, never a silent no-op.
        if (!TryParse(callback.Data, out var action, out var payload) ||
            callback.Message?.Chat is not { } chat)
        {
            await AcknowledgeClosedAsync(callback.Id, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Two resolution paths. A card action (F5) resolves its short id among the cards delivered within the
        // window the Telegram layer owns. A weekly rating (F4 T20) carries its own signed job id, so it resolves
        // from the payload alone — no candidate query and no window, because the Owner may rate a card up to a
        // week old and a stale tap must still land its Rated signal. Either way an unresolvable payload gets the
        // same plain message (AC-09).
        Guid jobId;
        string? applyUrl;
        if (action == CardAction.Rate)
        {
            var ratedJobId = _codec.ResolveRating(payload);
            if (ratedJobId is null)
            {
                await AcknowledgeClosedAsync(callback.Id, cancellationToken).ConfigureAwait(false);
                return;
            }

            jobId = ratedJobId.Value;
            applyUrl = null;
        }
        else
        {
            var candidates = await _cards
                .CandidatesSinceAsync(_clock.UtcNow - _window, cancellationToken)
                .ConfigureAwait(false);
            var key = _codec.Resolve(payload, candidates.Select(c => c.Key).ToArray());
            if (key is null)
            {
                await AcknowledgeClosedAsync(callback.Id, cancellationToken).ConfigureAwait(false);
                return;
            }

            var card = candidates.First(c => c.Key == key);
            jobId = card.JobId;
            applyUrl = card.ApplyUrl;
        }

        // Apply the action and capture the signal in one Application step (AC-08); the outcome tells us whether
        // the job was still live without this layer re-deriving the action's meaning.
        var outcome = await _action
            .Handle(new RecordCardActionCommand(jobId, action, _clock.UtcNow), cancellationToken)
            .ConfigureAwait(false);

        if (outcome == CardActionOutcome.JobUnavailable)
        {
            await AcknowledgeClosedAsync(callback.Id, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Callback for a closed job {JobId}; acknowledged as closed.", jobId);
            return;
        }

        // A first tap and an idempotent repeat both acknowledge and rewrite the keyboard identically (AC-03) —
        // the Owner gets the same feedback whether or not this was the call that recorded the signal.
        await _responder.AnswerCallbackAsync(callback.Id, AckTextFor(action), cancellationToken).ConfigureAwait(false);

        var keyboard = KeyboardFor(action, payload, applyUrl);
        if (keyboard is not null)
        {
            await _responder
                .EditReplyMarkupAsync(chat.Id, callback.Message.MessageId, keyboard, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogDebug("Recorded {Action} on job {JobId} ({Outcome}).", action, jobId, outcome);
    }

    private Task AcknowledgeClosedAsync(string callbackQueryId, CancellationToken cancellationToken) =>
        _responder.AnswerCallbackAsync(callbackQueryId, ClosedMessage, cancellationToken);

    // Parses "{action}:{shortId}" into a known action and its short id. A missing colon, an unknown action
    // token or an empty short id is not a card action — it is a value the caller turns into a plain message.
    private static bool TryParse(string? data, out CardAction action, out string shortId)
    {
        action = default;
        shortId = string.Empty;

        if (string.IsNullOrEmpty(data))
        {
            return false;
        }

        var separator = data.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == data.Length - 1)
        {
            return false;
        }

        var token = data[..separator];
        if (!TokenToAction.TryGetValue(token, out action))
        {
            return false;
        }

        shortId = data[(separator + 1)..];
        return true;
    }

    private static string? AckTextFor(CardAction action) => action switch
    {
        CardAction.Open => null,
        CardAction.Ignore => "Won't show similar",
        CardAction.Save => "Saved",
        CardAction.Applied => "Marked as applied",
        CardAction.Rate => "Thanks — noted",
        _ => null,
    };

    // The keyboard each action leaves behind (contract §Callback payloads). Open changes nothing, so it edits
    // no keyboard; the others rewrite the card to reflect the state the tap put it in. A rating payload carries
    // its own signed job id, so its rewritten button re-signs from the same payload the tap arrived with.
    private static IReadOnlyList<IReadOnlyList<InlineButton>>? KeyboardFor(CardAction action, string payload, string? applyUrl) =>
        action switch
        {
            CardAction.Ignore =>
                [[new InlineButton("Ignored", $"{ActionToken.Ignore}:{payload}")]],
            CardAction.Save =>
                [[
                    InlineButton.ForUrl("Open", applyUrl!),
                    new InlineButton("Saved ✓", $"{ActionToken.Save}:{payload}"),
                    new InlineButton("Applied", $"{ActionToken.Applied}:{payload}"),
                ]],
            CardAction.Applied =>
                [[
                    InlineButton.ForUrl("Open", applyUrl!),
                    new InlineButton("Applied ✓", $"{ActionToken.Applied}:{payload}"),
                ]],
            CardAction.Rate =>
                [[new InlineButton("Rated 👍", $"{ActionToken.Rate}:{payload}")]],
            _ => null,
        };

    // The 3-character action tokens fixed by the contract (open|ign|sav|app), plus the weekly rating token.
    private static class ActionToken
    {
        public const string Open = "open";
        public const string Ignore = "ign";
        public const string Save = "sav";
        public const string Applied = "app";
        public const string Rate = "rat";
    }

    private static readonly Dictionary<string, CardAction> TokenToAction = new(StringComparer.Ordinal)
    {
        [ActionToken.Open] = CardAction.Open,
        [ActionToken.Ignore] = CardAction.Ignore,
        [ActionToken.Save] = CardAction.Save,
        [ActionToken.Applied] = CardAction.Applied,
        [ActionToken.Rate] = CardAction.Rate,
    };
}
