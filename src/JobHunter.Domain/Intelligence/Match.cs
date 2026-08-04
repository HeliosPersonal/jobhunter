using System.Collections.ObjectModel;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The Stage-5 assessment of <em>fit</em> between a specific <see cref="CvVersion"/> and a job — the
/// model's judgement, not the final ordering number (data-model §matches). It carries a 0–100 match
/// score, an interview-probability band, the skills the CV is missing for the role, an optional salary
/// expectation the Owner could plausibly ask for, and the reasons behind the number.
///
/// <para>The construction guard is the point of the type: a <see cref="Match"/> cannot exist without at
/// least one non-blank reason, so [[CONTEXT]] invariant 4 ("every Match carries at least one reason",
/// AC-02) is a property of the type rather than a check a caller can forget. The aggregate is immutable
/// except for <see cref="IsCurrent"/>, which is cleared — never deleted — when its CV version is
/// superseded (AC-08): a stale match remains the honest record of what was true then.</para>
/// </summary>
public sealed class Match : Entity
{
    /// <summary>The wire schema caps missing skills at 10 (match-schema §schema).</summary>
    public const int MaxMissingSkills = 10;

    /// <summary>The lowest legal match score.</summary>
    public const int MinScore = 0;

    /// <summary>The highest legal match score.</summary>
    public const int MaxScore = 100;

    private readonly List<string> _missingSkills = [];
    private readonly List<string> _reasons = [];

    public Match(
        Guid id,
        Guid jobId,
        Guid runId,
        Guid profileId,
        Guid cvVersionId,
        int matchScore,
        InterviewProbability interviewProbability,
        IReadOnlyList<string> missingSkills,
        SalaryExpectation? salaryExpectation,
        IReadOnlyList<string> reasons,
        string promptVersion,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A Match must reference a Job.", nameof(jobId));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A Match must belong to a Run.", nameof(runId));
        }

        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("A Match must reference a Profile.", nameof(profileId));
        }

        if (cvVersionId == Guid.Empty)
        {
            throw new ArgumentException("A Match must reference a CV version.", nameof(cvVersionId));
        }

        if (matchScore is < MinScore or > MaxScore)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matchScore),
                matchScore,
                $"A match score must be in [{MinScore}, {MaxScore}].");
        }

        ArgumentNullException.ThrowIfNull(missingSkills);
        ArgumentNullException.ThrowIfNull(reasons);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);

        var cleanedReasons = reasons
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToList();

        if (cleanedReasons.Count == 0)
        {
            // Invariant 4 as a type-level property: an unexplained fit judgement cannot be constructed.
            throw new ArgumentException(
                "A Match must carry at least one non-blank reason (invariant 4).",
                nameof(reasons));
        }

        _reasons = cleanedReasons;
        _missingSkills = missingSkills
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Take(MaxMissingSkills)
            .ToList();

        JobId = jobId;
        RunId = runId;
        ProfileId = profileId;
        CvVersionId = cvVersionId;
        MatchScore = matchScore;
        InterviewProbability = interviewProbability;
        SalaryExpectation = salaryExpectation;
        PromptVersion = promptVersion;
        CreatedAt = createdAt;
        IsCurrent = true;
    }

    private Match()
    {
    }

    public Guid JobId { get; private set; }

    public Guid RunId { get; private set; }

    public Guid ProfileId { get; private set; }

    public Guid CvVersionId { get; private set; }

    /// <summary>The model's 0–100 fit judgement — not the final ordering number, which ranking computes.</summary>
    public int MatchScore { get; private set; }

    public InterviewProbability InterviewProbability { get; private set; }

    /// <summary>What the Owner could plausibly ask for <em>this</em> role; null when the model cannot tell.</summary>
    public SalaryExpectation? SalaryExpectation { get; private set; }

    public string PromptVersion { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>True until the CV version this match was made against is superseded (AC-08).</summary>
    public bool IsCurrent { get; private set; }

    /// <summary>The skills the CV is missing for the role; may be empty, and empty is meaningful.</summary>
    public IReadOnlyList<string> MissingSkills => new ReadOnlyCollection<string>(_missingSkills);

    /// <summary>Non-empty by construction — invariant 4.</summary>
    public IReadOnlyList<string> Reasons => new ReadOnlyCollection<string>(_reasons);

    /// <summary>
    /// Marks this match stale because its CV version was superseded (AC-08). It is never deleted: a match
    /// against an older CV remains the record that explains why yesterday's digest said what it said.
    /// </summary>
    public void MarkNotCurrent() => IsCurrent = false;
}
