using JobHunter.Domain.Commands;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write port over <c>command_invocations</c> (F10 data-model §command_invocations). The dispatcher
/// records every command attempt through it — one row per dispatch, whatever the outcome. The log is
/// append-only: there is no read, update or delete path here, because F10 is a surface and the audit is a
/// fact about the past that the usage metric ([[PRD]] §7) reads elsewhere. Recording must never throw in
/// a way that reaches the Owner — a failed audit is an operational fault, not a failed command.
/// </summary>
public interface ICommandInvocationLog
{
    /// <summary>Appends <paramref name="invocation"/> to the audit log.</summary>
    Task RecordAsync(CommandInvocation invocation, CancellationToken cancellationToken = default);
}
