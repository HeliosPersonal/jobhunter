using JobHunter.Domain.Applications;

namespace JobHunter.Application.Applications;

/// <summary>
/// Which of the three business outcomes a <see cref="ChangeApplicationStatusCommand"/> produced — a value,
/// not an exception, because each is an expected result the caller renders (coding-standards §4). The API
/// maps them to <c>200</c>, <c>409</c> and <c>404</c> respectively; Telegram maps them to distinct replies.
/// </summary>
public enum ChangeApplicationStatusResult
{
    /// <summary>The transition was permitted and applied; the application advanced.</summary>
    Changed,

    /// <summary>The transition is impossible for the current status; nothing changed. <see
    /// cref="ChangeApplicationStatusOutcome.Remedy"/> names what to do instead (AC-10).</summary>
    NotPermitted,

    /// <summary>No application tracks the job yet — a status change annotates a tracked job, it does not create one.</summary>
    ApplicationNotFound,
}

/// <summary>
/// The result of a <see cref="ChangeApplicationStatusHandler"/> invocation. It carries enough for the API's
/// <c>200</c>/<c>409</c> bodies and Telegram's reply without a second read: the <see cref="From"/>/<see
/// cref="To"/> pair the caller echoes, the <see cref="ApplicationId"/> a success advanced, and — on a refusal
/// — the <see cref="Remedy"/> that names the rule and the fix (AC-10). A refusal without a remedy is just an
/// obstacle, so <see cref="NotPermitted"/> always carries one.
/// </summary>
/// <param name="Result">Which of the three outcomes occurred.</param>
/// <param name="From">The status the application was in (the current status on a refusal or not-found).</param>
/// <param name="To">The requested target status.</param>
/// <param name="ApplicationId">The application affected; <c>null</c> when none was found.</param>
/// <param name="Remedy">On <see cref="ChangeApplicationStatusResult.NotPermitted"/>, what to do instead; otherwise <c>null</c>.</param>
public sealed record ChangeApplicationStatusOutcome(
    ChangeApplicationStatusResult Result,
    ApplicationStatus? From,
    ApplicationStatus To,
    Guid? ApplicationId,
    string? Remedy)
{
    public static ChangeApplicationStatusOutcome Changed(Guid applicationId, ApplicationStatus from, ApplicationStatus to) =>
        new(ChangeApplicationStatusResult.Changed, from, to, applicationId, Remedy: null);

    public static ChangeApplicationStatusOutcome NotPermitted(ApplicationStatus from, ApplicationStatus to, string remedy) =>
        new(ChangeApplicationStatusResult.NotPermitted, from, to, ApplicationId: null, remedy);

    public static ChangeApplicationStatusOutcome NotFound(ApplicationStatus to) =>
        new(ChangeApplicationStatusResult.ApplicationNotFound, From: null, to, ApplicationId: null, Remedy: null);
}
