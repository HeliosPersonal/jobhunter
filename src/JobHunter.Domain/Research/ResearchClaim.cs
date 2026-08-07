using JobHunter.Domain.Common;

namespace JobHunter.Domain.Research;

/// <summary>
/// One asserted fact about a company, resting on exactly one <see cref="ResearchSource"/>. This is where
/// [[CONTEXT]] invariant 5 lives as a type-level property (AC-02): the constructor takes a source object,
/// not a bare id, so an uncited claim cannot be constructed — it is unrepresentable, not merely rejected.
///
/// <para>The observed date is <em>copied</em> from the source (AC-03), never accepted independently, so a
/// claim can never present itself as fresher than the document it rests on. The claim's own
/// <see cref="Category"/> is authoritative and need not equal the fetching source's category — a news
/// document can substantiate a layoffs claim.</para>
/// </summary>
public sealed class ResearchClaim : Entity
{
    public ResearchClaim(
        Guid id,
        ResearchSource source,
        ResearchCategory category,
        string claim,
        bool isWarning)
        : base(id)
    {
        // Invariant 5: no source object, no claim. The type makes an uncited claim impossible to express.
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(claim);

        SourceId = source.Id;
        Category = category;
        Claim = claim.Trim();
        IsWarning = isWarning;
        ObservedAt = source.ObservedAt;
    }

    private ResearchClaim()
    {
    }

    /// <summary>The source this claim cites — a foreign key that cannot be null (invariant 5).</summary>
    public Guid SourceId { get; private set; }

    /// <summary>The category this claim belongs to; authoritative, independent of the source's category.</summary>
    public ResearchCategory Category { get; private set; }

    /// <summary>The one-sentence claim.</summary>
    public string Claim { get; private set; } = null!;

    /// <summary>Whether this is a warning (layoffs, funding difficulty) — surfaced first (AC-04).</summary>
    public bool IsWarning { get; private set; }

    /// <summary>The observed date, copied from the cited source (AC-03).</summary>
    public DateTimeOffset ObservedAt { get; private set; }
}
