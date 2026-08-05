using System.ComponentModel.DataAnnotations;

namespace JobHunter.Telegram;

/// <summary>
/// The bot's connection, allowlist and pacing knobs (ADR-0014, SAD §7). Bound and validated at startup via
/// <c>.Validate().ValidateOnStart()</c> — a missing token or an empty allowlist fails the pod at boot,
/// never silently at the first send or the first update (coding-standards §3). The token is a secret: it is
/// carried only in the notifier's HTTP base address and never logged, never put in an exception message and
/// never on a span (invariant 12).
/// </summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>The bot API token from BotFather. A secret — never logged (invariant 12).</summary>
    [Required(AllowEmptyStrings = false)]
    public string BotToken { get; init; } = string.Empty;

    /// <summary>
    /// The chat ids allowed to reach a handler (ADR-0014). The system is single-Owner (invariant 9), so this
    /// is normally one id; an update from any other chat is dropped before routing and its id logged at
    /// warning level (AC-10).
    /// </summary>
    [MinLength(1)]
    public IReadOnlyList<long> AllowedChatIds { get; init; } = [];

    /// <summary>
    /// The long-poll wait in seconds passed to <c>getUpdates</c>. Telegram holds the request open this long
    /// when no update is pending, so the loop is not a busy spin.
    /// </summary>
    [Range(1, 50)]
    public int LongPollTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// The minimum gap between two sends, holding the sender inside the 30-messages-per-second global limit
    /// (SAD §7). The pacer never sends faster than this; a <c>429</c> overrides it upward for that host.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:00:05")]
    public TimeSpan MinSendInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>The delay after a network interruption before the long-poll loop reconnects.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:01:00")]
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How many times a single send is retried on a <c>429</c> before it is abandoned as failed.</summary>
    [Range(1, 10)]
    public int MaxSendAttempts { get; init; } = 5;
}
