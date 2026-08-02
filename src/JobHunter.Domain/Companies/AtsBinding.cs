using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Companies;

/// <summary>
/// Where a company's jobs actually live, plus the evidence for believing it (data-model §ats_bindings).
/// A binding is never deleted: an ATS migration <see cref="Retire"/>s the old one and records a new one,
/// which is what makes the migration auditable (AC-05). The evidence is retained verbatim as a JSON
/// document so a wrong binding is debuggable without re-running detection.
/// </summary>
public sealed class AtsBinding : Entity
{
    public static readonly Error EmptyBoardToken =
        new("binding.board_token.empty", "A binding requires a non-empty board token.");

    public AtsBinding(
        Guid id,
        Guid companyId,
        AtsKind atsKind,
        string boardToken,
        BindingConfidence confidence,
        string evidence,
        DateTimeOffset detectedAt)
        : base(id)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Binding company id must not be empty.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(boardToken);
        ArgumentNullException.ThrowIfNull(confidence);
        ArgumentNullException.ThrowIfNull(evidence);

        CompanyId = companyId;
        AtsKind = atsKind;
        BoardToken = boardToken;
        Confidence = confidence;
        Evidence = evidence;
        DetectedAt = detectedAt;
    }

    private AtsBinding()
    {
        BoardToken = string.Empty;
        Evidence = string.Empty;
        Confidence = null!;
    }

    public Guid CompanyId { get; private set; }

    public AtsKind AtsKind { get; private set; }

    public string BoardToken { get; private set; }

    public BindingConfidence Confidence { get; private set; }

    /// <summary>The probe trail as a JSON document (data-model: <c>evidence jsonb NOT NULL</c>).</summary>
    public string Evidence { get; private set; }

    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>Set once when the binding is retired on an ATS migration; never cleared.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>True while the binding is the live one for its provider (not retired).</summary>
    public bool IsLive => RetiredAt is null;

    /// <summary>
    /// Creates a validated binding, or a failure if the board token is blank. Preferred over the
    /// constructor on the detection path, where a blank token is an expected (recorded) outcome.
    /// </summary>
    public static Result<AtsBinding> TryCreate(
        Guid id,
        Guid companyId,
        AtsKind atsKind,
        string boardToken,
        BindingConfidence confidence,
        string evidence,
        DateTimeOffset detectedAt)
    {
        if (string.IsNullOrWhiteSpace(boardToken))
        {
            return EmptyBoardToken;
        }

        return Result<AtsBinding>.Success(
            new AtsBinding(id, companyId, atsKind, boardToken, confidence, evidence, detectedAt));
    }

    /// <summary>
    /// Retires the binding as of the clock's instant. Idempotent — retiring an already-retired binding
    /// keeps the original retirement time (the migration happened once).
    /// </summary>
    public void Retire(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        RetiredAt ??= clock.UtcNow;
    }
}
