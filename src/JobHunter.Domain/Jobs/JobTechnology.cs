namespace JobHunter.Domain.Jobs;

/// <summary>
/// A deterministic, vocabulary-matched technology tag on a job (data-model §job_technologies). The
/// <see cref="Technology"/> is the canonical vocabulary name (<c>C#</c>, not <c>csharp</c>), so the same
/// skill from two postings tags identically. F3 later adds model-extracted technologies elsewhere and
/// never writes here, keeping the deterministic set separable from the inferred one.
/// </summary>
public sealed class JobTechnology
{
    public JobTechnology(Guid jobId, string technology, TechnologyMatch matchedVia)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Technology job id must not be empty.", nameof(jobId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(technology);

        JobId = jobId;
        Technology = technology;
        MatchedVia = matchedVia;
    }

    private JobTechnology()
    {
        Technology = string.Empty;
    }

    public Guid JobId { get; private set; }

    /// <summary>The canonical vocabulary name.</summary>
    public string Technology { get; private set; }

    public TechnologyMatch MatchedVia { get; private set; }
}
