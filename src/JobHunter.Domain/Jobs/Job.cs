using JobHunter.Domain.Common;

namespace JobHunter.Domain.Jobs;

/// <summary>
/// The canonical vacancy: one row per real opening (invariant 2), assembled by normalising a raw posting
/// and keyed by a conservative <see cref="Fingerprint"/>. The same opening seen on several boards is one
/// <c>Job</c> with several <see cref="JobAlias"/> rows, never several jobs — the feature exists to make
/// that so with zero false merges (ADR-F2-0001).
///
/// <para><see cref="Title"/> is preserved exactly as published — it is what the Owner reads — while
/// <see cref="NormalisedTitle"/> is a comparison form that is never displayed (data-model §jobs). The
/// aggregate takes no ambient clock: every timestamp is passed in, so the same inputs always produce the
/// same job (coding-standards §5).</para>
/// </summary>
public sealed class Job : Entity
{
    public static readonly Error CannotCloseQuarantined =
        new("job.close.quarantined", "A quarantined job cannot be closed; resolve the quarantine first.");

    public static readonly Error CannotReopenQuarantined =
        new("job.reopen.quarantined", "A quarantined job cannot be reopened; resolve the quarantine first.");

    public static readonly Error CannotSupersedeQuarantined =
        new("job.supersede.quarantined", "A quarantined job cannot be superseded; resolve the quarantine first.");

    public static readonly Error IsSuperseded =
        new("job.superseded", "A superseded job is terminal; it cannot be closed or reopened.");

    private readonly List<JobAlias> _aliases = [];
    private readonly List<JobTechnology> _technologies = [];

    public Job(
        Guid id,
        Guid companyId,
        Guid originRawPostingId,
        Fingerprint fingerprint,
        short fingerprintVersion,
        string title,
        string normalisedTitle,
        string description,
        string applyUrl,
        LocationSet locations,
        RemotePolicy remotePolicy,
        EmploymentType employmentType,
        PostedAtGranularity postedAtGranularity,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt,
        Seniority? seniority = null,
        SalaryRange? salary = null,
        string? salaryRaw = null,
        DateTimeOffset? postedAt = null,
        bool isTier2 = false,
        JobStatus status = JobStatus.Live)
        : base(id)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Job company id must not be empty.", nameof(companyId));
        }

        if (originRawPostingId == Guid.Empty)
        {
            throw new ArgumentException("Job origin raw posting id must not be empty.", nameof(originRawPostingId));
        }

        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalisedTitle);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(applyUrl);
        ArgumentNullException.ThrowIfNull(locations);

        CompanyId = companyId;
        OriginRawPostingId = originRawPostingId;
        Fingerprint = fingerprint;
        FingerprintVersion = fingerprintVersion;
        Title = title;
        NormalisedTitle = normalisedTitle;
        Description = description;
        ApplyUrl = applyUrl;
        Locations = locations;
        RemotePolicy = remotePolicy;
        EmploymentType = employmentType;
        PostedAtGranularity = postedAtGranularity;
        Seniority = seniority;
        Salary = salary;
        SalaryRaw = salaryRaw;
        PostedAt = postedAt;
        IsTier2 = isTier2;
        Status = status;
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
    }

    private Job()
    {
        Fingerprint = null!;
        Title = string.Empty;
        NormalisedTitle = string.Empty;
        Description = string.Empty;
        ApplyUrl = string.Empty;
        Locations = LocationSet.Empty;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The posting that first created this job.</summary>
    public Guid OriginRawPostingId { get; private set; }

    public Fingerprint Fingerprint { get; private set; }

    /// <summary>Bumped when the fingerprint algorithm changes (SAD §11).</summary>
    public short FingerprintVersion { get; private set; }

    /// <summary>As published — never modified, this is what the Owner reads.</summary>
    public string Title { get; private set; }

    /// <summary>Comparison form only, never displayed.</summary>
    public string NormalisedTitle { get; private set; }

    public Seniority? Seniority { get; private set; }

    /// <summary>HTML stripped to plain text at the boundary.</summary>
    public string Description { get; private set; }

    public string ApplyUrl { get; private set; }

    public LocationSet Locations { get; private set; }

    public RemotePolicy RemotePolicy { get; private set; }

    public EmploymentType EmploymentType { get; private set; }

    /// <summary>The published pay range, or null when none was published.</summary>
    public SalaryRange? Salary { get; private set; }

    /// <summary>The raw salary text, retained when unparseable so nothing is lost to a parser gap.</summary>
    public string? SalaryRaw { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public PostedAtGranularity PostedAtGranularity { get; private set; }

    public DateTimeOffset FirstSeenAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public JobStatus Status { get; private set; }

    /// <summary>
    /// When <see cref="JobStatus.Superseded"/>, the id of the job that replaced this one after a
    /// reprocessing fingerprint change (AC-09); null otherwise. The superseded row is retained, not deleted,
    /// so downstream references resolve to a successor rather than dangling.
    /// </summary>
    public Guid? SupersededBy { get; private set; }

    /// <summary>True when this job originated from a JSON-LD career page (Tier 2, lower confidence).</summary>
    public bool IsTier2 { get; private set; }

    /// <summary>The provenance trail — every raw posting that ever contributed to this job.</summary>
    public IReadOnlyList<JobAlias> Aliases => _aliases;

    /// <summary>The deterministic, vocabulary-matched technology tags.</summary>
    public IReadOnlyList<JobTechnology> Technologies => _technologies;

    /// <summary>
    /// Records a raw posting as contributing to this job. Idempotent per raw posting: a posting already
    /// registered has its <c>last_seen_at</c> bumped instead of being added twice (data-model
    /// §job_aliases — one row per raw posting). Never moves <see cref="LastSeenAt"/> backwards.
    /// </summary>
    public JobAlias RegisterAlias(
        Guid rawPostingId,
        Guid sourceId,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt)
    {
        var existing = _aliases.FirstOrDefault(a => a.RawPostingId == rawPostingId);
        if (existing is not null)
        {
            existing.Touch(lastSeenAt);
            BumpLastSeen(lastSeenAt);
            return existing;
        }

        var alias = new JobAlias(Id, rawPostingId, sourceId, firstSeenAt, lastSeenAt);
        _aliases.Add(alias);
        BumpLastSeen(lastSeenAt);
        return alias;
    }

    /// <summary>
    /// Adds a technology tag, idempotent by canonical name: the same technology from a second source is
    /// not duplicated, keeping the primary key <c>(job_id, technology)</c> honest.
    /// </summary>
    public JobTechnology AddTechnology(string technology, TechnologyMatch matchedVia)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(technology);

        var existing = _technologies.FirstOrDefault(
            t => string.Equals(t.Technology, technology, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        var tag = new JobTechnology(Id, technology, matchedVia);
        _technologies.Add(tag);
        return tag;
    }

    /// <summary>
    /// Closes the job at <paramref name="at"/>. Idempotent — closing an already-closed job is a no-op that
    /// keeps the original <see cref="ClosedAt"/>. A <see cref="JobStatus.Quarantined"/> job cannot be
    /// closed: quarantine is resolved by a human, never by the closure sweep (data-model §jobs).
    /// </summary>
    public Result<Job> Close(DateTimeOffset at)
    {
        switch (Status)
        {
            case JobStatus.Quarantined:
                return CannotCloseQuarantined;
            case JobStatus.Superseded:
                return IsSuperseded;
            case JobStatus.Closed:
                return Result<Job>.Success(this);
            default:
                Status = JobStatus.Closed;
                ClosedAt = at;
                return Result<Job>.Success(this);
        }
    }

    /// <summary>
    /// Reopens a closed job at <paramref name="at"/>, clearing <see cref="ClosedAt"/> and bumping
    /// liveness. Idempotent — reopening an already-live job is a no-op. A quarantined job cannot be
    /// reopened this way.
    /// </summary>
    public Result<Job> Reopen(DateTimeOffset at)
    {
        switch (Status)
        {
            case JobStatus.Quarantined:
                return CannotReopenQuarantined;
            case JobStatus.Superseded:
                return IsSuperseded;
            case JobStatus.Live:
                return Result<Job>.Success(this);
            default:
                Status = JobStatus.Live;
                ClosedAt = null;
                BumpLastSeen(at);
                return Result<Job>.Success(this);
        }
    }

    /// <summary>
    /// Retires this job in favour of <paramref name="successorId"/> after a reprocessing fingerprint change
    /// (AC-09). The row is kept — its provenance and any downstream references still resolve — and
    /// <see cref="SupersededBy"/> records where the opening moved to, rather than orphaning it silently.
    /// Idempotent: superseding an already-superseded job keeps the first successor. A quarantined job is
    /// left withheld and refuses, exactly as closure does; a live or closed job may be superseded.
    /// </summary>
    public Result<Job> Supersede(Guid successorId, DateTimeOffset at)
    {
        if (successorId == Guid.Empty)
        {
            throw new ArgumentException("Successor job id must not be empty.", nameof(successorId));
        }

        switch (Status)
        {
            case JobStatus.Quarantined:
                return CannotSupersedeQuarantined;
            case JobStatus.Superseded:
                return Result<Job>.Success(this);
            default:
                Status = JobStatus.Superseded;
                SupersededBy = successorId;
                ClosedAt = at;
                return Result<Job>.Success(this);
        }
    }

    /// <summary>
    /// Withholds the job from the pipeline pending human review. Idempotent, and legal from any state —
    /// quarantine is the safe stop that both closure and reopening then refuse to override.
    /// </summary>
    public void Quarantine() => Status = JobStatus.Quarantined;

    private void BumpLastSeen(DateTimeOffset seenAt)
    {
        if (seenAt > LastSeenAt)
        {
            LastSeenAt = seenAt;
        }
    }
}
