using JobHunter.Domain.Commands;

namespace JobHunter.Application.Commands;

/// <summary>
/// The resolved head of a dispatch turn (SAD §6.2): the <see cref="Disposition"/> the dispatcher acts on,
/// the <see cref="Pending"/> state it concerns (null when nothing was pending), and — for a
/// <see cref="ConversationDisposition.Resume"/> — the <see cref="Input"/> that the pending command should
/// resume with, which is the incoming message verbatim.
/// </summary>
public sealed record ConversationTurn
{
    private ConversationTurn(ConversationDisposition disposition, ConversationState? pending, string? input)
    {
        Disposition = disposition;
        Pending = pending;
        Input = input;
    }

    public ConversationDisposition Disposition { get; }

    /// <summary>The pending conversation this turn concerns; null when nothing was pending.</summary>
    public ConversationState? Pending { get; }

    /// <summary>The resume input for a <see cref="ConversationDisposition.Resume"/>; null otherwise.</summary>
    public string? Input { get; }

    internal static ConversationTurn Proceed() =>
        new(ConversationDisposition.Proceed, pending: null, input: null);

    internal static ConversationTurn NothingToCancel() =>
        new(ConversationDisposition.NothingToCancel, pending: null, input: null);

    internal static ConversationTurn Resume(ConversationState pending, string input) =>
        new(ConversationDisposition.Resume, pending, input);

    internal static ConversationTurn For(ConversationDisposition disposition, ConversationState pending) =>
        new(disposition, pending, input: null);
}
