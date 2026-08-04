using System.Collections.ObjectModel;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Reporting;

/// <summary>
/// One ranked job in a <see cref="Digest"/> (data-model §digest_cards). It snapshots the score and reasons
/// at assembly time, so the card is stable even if scoring is later re-run — a replayed digest shows what
/// the Owner was actually sent, not what a recomputation would say now.
///
/// <para>The construction guard is the point of the type: a card cannot exist without at least one non-blank
/// reason, so [[CONTEXT]] invariant 4 ("an unexplained number never reaches the Owner", AC-02) is a property
/// of the type rather than a check a caller can forget. Its <see cref="CardKey"/> is the deterministic
/// idempotence key that lets a resumed delivery skip what it already sent (ADR-F5-0002).</para>
/// </summary>
public sealed class DigestCard : Entity
{
    /// <summary>The lowest legal card score.</summary>
    public const decimal MinScore = 0m;

    /// <summary>The highest legal card score.</summary>
    public const decimal MaxScore = 100m;

    private readonly List<string> _reasons = [];

    public DigestCard(
        Guid id,
        Guid digestId,
        Guid jobId,
        Guid runId,
        int rank,
        decimal score,
        IReadOnlyList<string> reasons,
        bool applyUrlVerified)
        : base(id)
    {
        if (digestId == Guid.Empty)
        {
            throw new ArgumentException("A DigestCard must belong to a Digest.", nameof(digestId));
        }

        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A DigestCard must reference a Job.", nameof(jobId));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A DigestCard must reference a Run.", nameof(runId));
        }

        if (rank < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "A card rank is 1-based.");
        }

        if (score is < MinScore or > MaxScore)
        {
            throw new ArgumentOutOfRangeException(
                nameof(score),
                score,
                $"A card score must be in [{MinScore}, {MaxScore}].");
        }

        ArgumentNullException.ThrowIfNull(reasons);

        var cleanedReasons = reasons
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToList();

        if (cleanedReasons.Count == 0)
        {
            // Invariant 4 / AC-02 as a type-level property: an unexplained card cannot be constructed.
            throw new ArgumentException(
                "A DigestCard must carry at least one non-blank reason (invariant 4).",
                nameof(reasons));
        }

        _reasons = cleanedReasons;
        DigestId = digestId;
        JobId = jobId;
        Rank = rank;
        Score = score;
        ApplyUrlVerified = applyUrlVerified;
        Key = CardKey.For(runId, jobId);
    }

    private DigestCard()
    {
    }

    public Guid DigestId { get; private set; }

    public Guid JobId { get; private set; }

    /// <summary>1-based presentation order within the digest.</summary>
    public int Rank { get; private set; }

    /// <summary>The final ordering score, snapshotted so the card is stable across a re-score.</summary>
    public decimal Score { get; private set; }

    /// <summary>False when the apply link could not be confirmed reachable; a confirmed-unreachable card is not delivered (AC-11).</summary>
    public bool ApplyUrlVerified { get; private set; }

    /// <summary>The deterministic idempotence key, a pure function of <c>(run_id, job_id)</c> (ADR-F5-0002).</summary>
    public CardKey Key { get; private set; } = null!;

    /// <summary>Non-empty by construction — invariant 4.</summary>
    public IReadOnlyList<string> Reasons => new ReadOnlyCollection<string>(_reasons);
}
