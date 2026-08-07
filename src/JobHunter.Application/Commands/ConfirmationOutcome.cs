namespace JobHunter.Application.Commands;

/// <summary>
/// What a confirmation tap resolves to (SAD §6.3). The Telegram layer turns it into either running the
/// confirmed command or one of three refusals, each with its own reply: an expired token asks the Owner
/// to re-issue, an already-used one says so, a chat mismatch is refused silently. Keeping it a value means
/// the single-use and expiry rules are decided in <see cref="ConfirmationService"/> and unit-tested with
/// no chat and no real time.
/// </summary>
public enum ConfirmationOutcome
{
    /// <summary>Unset — never returned; guards against a default slipping through.</summary>
    Unspecified = 0,

    /// <summary>The tap is valid and unused — run the confirmed command with its argument tail.</summary>
    Confirmed = 1,

    /// <summary>No live token for the nonce — expired by TTL or never issued; ask the Owner to re-issue.</summary>
    Expired = 2,

    /// <summary>The token was already redeemed — a second tap; say it was already used.</summary>
    AlreadyUsed = 3,

    /// <summary>The tap came from a different chat than the token was issued to — refused.</summary>
    Mismatch = 4,
}
