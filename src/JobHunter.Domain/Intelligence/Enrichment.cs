using System.Collections.ObjectModel;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The Stage-4 assessment of a <em>job</em> — never of the fit, which is F4's Match (data-model
/// §enrichments). It describes what the role is: an estimated salary, remoteness, contractor
/// friendliness, timezone band, AI-usage level, company stage and extracted technologies.
///
/// <para>The construction guard is the point of the type: an <see cref="Enrichment"/> cannot exist
/// without at least one non-blank reason, so [[CONTEXT]] invariant 4 ("every Enrichment carries at
/// least one reason") is a property of the type rather than a validation step a caller can forget
/// (AC-02). The aggregate is immutable — a correction is a new row for a new Run, guaranteed distinct
/// by the unique <c>(job_id, run_id)</c> index (invariant 3).</para>
/// </summary>
public sealed class Enrichment : Entity
{
    private readonly List<string> _reasons = [];
    private readonly List<string> _technologies = [];

    public Enrichment(
        Guid id,
        Guid jobId,
        Guid runId,
        SalaryEstimate? salary,
        bool isRemote,
        bool isContractorFriendly,
        TimezoneBand timezoneBand,
        AiUsageLevel aiUsage,
        AiSignals aiSignals,
        CompanyStage companyStage,
        RoleFamily roleFamily,
        IReadOnlyList<string> technologies,
        IReadOnlyList<string> reasons,
        string promptVersion,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("An Enrichment must reference a Job.", nameof(jobId));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("An Enrichment must belong to a Run.", nameof(runId));
        }

        ArgumentNullException.ThrowIfNull(reasons);
        ArgumentNullException.ThrowIfNull(technologies);
        ArgumentNullException.ThrowIfNull(aiSignals);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);

        var cleanedReasons = reasons
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToList();

        if (cleanedReasons.Count == 0)
        {
            // Invariant 4 as a type-level property: an unexplained assessment cannot be constructed.
            throw new ArgumentException(
                "An Enrichment must carry at least one non-blank reason (invariant 4).",
                nameof(reasons));
        }

        _reasons = cleanedReasons;
        _technologies = technologies
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Take(MaxTechnologies)
            .ToList();

        JobId = jobId;
        RunId = runId;
        Salary = salary;
        IsRemote = isRemote;
        IsContractorFriendly = isContractorFriendly;
        TimezoneBand = timezoneBand;
        AiUsage = aiUsage;
        AiSignals = aiSignals;
        CompanyStage = companyStage;
        RoleFamily = roleFamily;
        PromptVersion = promptVersion;
        CreatedAt = createdAt;
    }

    private Enrichment()
    {
    }

    /// <summary>The wire schema caps technologies at 25 (enrichment-schema §schema).</summary>
    public const int MaxTechnologies = 25;

    public Guid JobId { get; private set; }

    public Guid RunId { get; private set; }

    /// <summary>The model's estimate; null when the model genuinely cannot tell (schema allows null).</summary>
    public SalaryEstimate? Salary { get; private set; }

    public bool IsRemote { get; private set; }

    public bool IsContractorFriendly { get; private set; }

    public TimezoneBand TimezoneBand { get; private set; }

    public AiUsageLevel AiUsage { get; private set; }

    /// <summary>The resolving AI sub-signals that separate building-with-AI from merely using AI tooling (TUNE-04).</summary>
    public AiSignals AiSignals { get; private set; } = AiSignals.None;

    public CompanyStage CompanyStage { get; private set; }

    /// <summary>What the role is, classified from the described work — the F4 alignment signal (TUNE-03).</summary>
    public RoleFamily RoleFamily { get; private set; }

    public string PromptVersion { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Model-extracted technology names, kept separate from F2's deterministic set.</summary>
    public IReadOnlyList<string> Technologies => new ReadOnlyCollection<string>(_technologies);

    /// <summary>Non-empty by construction — invariant 4.</summary>
    public IReadOnlyList<string> Reasons => new ReadOnlyCollection<string>(_reasons);
}
