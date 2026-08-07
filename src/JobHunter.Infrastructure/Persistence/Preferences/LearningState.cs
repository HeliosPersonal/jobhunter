namespace JobHunter.Infrastructure.Persistence.Preferences;

/// <summary>
/// The single persisted row behind <see cref="PersistentLearningSwitch"/> (F7 T08 C4, AC-07). One Owner means
/// one flag: the row is keyed by a well-known fixed id so there is at most one, and its absence means "never
/// flipped — use the configured seed default". A pure Infrastructure persistence detail, not a Domain
/// aggregate: the master switch is operational state the API and Telegram surfaces flip, carrying no invariant
/// the Domain must guard, so it lives with its EF configuration rather than in <c>Domain</c>.
/// </summary>
internal sealed class LearningState
{
    /// <summary>The one row's fixed identity — there is a single Owner, so a single switch (invariant 9).</summary>
    public static readonly Guid SingletonId = new("f7000000-0000-0000-0000-000000000001");

    public LearningState(Guid id, bool enabled, DateTimeOffset updatedAt)
    {
        Id = id;
        Enabled = enabled;
        UpdatedAt = updatedAt;
    }

    /// <summary>EF Core materialisation constructor.</summary>
    private LearningState()
    {
    }

    public Guid Id { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Set(bool enabled, DateTimeOffset updatedAt)
    {
        Enabled = enabled;
        UpdatedAt = updatedAt;
    }
}
