using System.Collections.Frozen;
using Microsoft.Extensions.Options;

namespace JobHunter.Telegram.Auth;

/// <summary>
/// The chat-id allowlist at the very front of the update pipeline (ADR-0014, SAD §6.2). Every inbound
/// update passes <see cref="IsOwner"/> before any handler runs; an update whose chat id is not on the
/// allowlist is dropped and its id logged at warning level (AC-10), so an unauthorised update physically
/// cannot reach the domain. The system is single-Owner (invariant 9): there are no roles, only "is this the
/// Owner's chat".
/// </summary>
internal sealed class OwnerAuthorizer
{
    private readonly FrozenSet<long> _allowed;
    private readonly ILogger<OwnerAuthorizer> _logger;

    public OwnerAuthorizer(IOptions<TelegramOptions> options, ILogger<OwnerAuthorizer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _allowed = options.Value.AllowedChatIds.ToFrozenSet();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// True when <paramref name="chatId"/> is the Owner's. A rejected id is logged once at warning level and
    /// nothing else about the update is touched — no body is read, no handler is resolved (AC-10).
    /// </summary>
    public bool IsOwner(long chatId)
    {
        if (_allowed.Contains(chatId))
        {
            return true;
        }

        _logger.LogWarning("Dropped an update from an unauthorised chat {ChatId} before routing.", chatId);
        return false;
    }
}
