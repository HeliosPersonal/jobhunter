using JobHunter.Domain.Common;

namespace JobHunter.Domain.Applications;

/// <summary>
/// A free-text note the Owner attaches to an <see cref="Application"/> (F6 [[data-model]] §application_notes)
/// — "phone screen went well", "recruiter ghosted". Notes are additive history alongside the transition
/// log, capped at <see cref="MaxLength"/> characters. The body never enters a log or span; only its length
/// is ever observable (invariant 12).
/// </summary>
public sealed class ApplicationNote : Entity
{
    /// <summary>The longest a note body may be; a longer body is rejected at the boundary.</summary>
    public const int MaxLength = 4000;

    public ApplicationNote(Guid id, Guid applicationId, string body, DateTimeOffset createdAt)
        : base(id)
    {
        if (applicationId == Guid.Empty)
        {
            throw new ArgumentException("A note must reference an application.", nameof(applicationId));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("A note body must not be blank.", nameof(body));
        }

        if (body.Length > MaxLength)
        {
            throw new ArgumentException(
                $"A note body must not exceed {MaxLength} characters.",
                nameof(body));
        }

        ApplicationId = applicationId;
        Body = body;
        CreatedAt = createdAt;
    }

    private ApplicationNote()
    {
    }

    public Guid ApplicationId { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
}
