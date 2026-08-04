using System.Globalization;
using JobHunter.Claude.Prompts;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;

namespace JobHunter.Claude.Matching;

/// <summary>
/// Renders the matching batch request from a Run's in-scope jobs, the active <see cref="Profile"/> and the
/// active <see cref="CvVersion"/> (F4 SAD §6.1, T05). It is the Claude-side implementation of
/// <see cref="IMatchRequestBuilder"/>: the Application submit handler holds the cost gate and the
/// persistence, and delegates the one thing that must know the prompt and the schema — building the items —
/// to here, so the versioned prompt (<see cref="MatchPrompt"/>), the generated schema
/// (<see cref="MatchSchema"/>) and the prompt version stay in one place and the Application layer stays free
/// of any Anthropic concept (architecture rule 3).
///
/// <para><strong>This type, through <see cref="MatchPrompt"/>, is the one boundary the CV crosses.</strong>
/// <see cref="CvVersion.ExtractedText"/> is read here and folded into each item's cache prefix and nowhere
/// else. The candidate block is identical across every item — it is the shared prompt-cache prefix (T13) —
/// so it is carried on <see cref="BatchRequestItem.CachePrefix"/>, where the adapter places the
/// <c>cache_control</c> breakpoint at its end; the role block is the per-item
/// <see cref="BatchRequestItem.UserContent"/>. <see cref="BatchRequestItem.CustomId"/> is the job id
/// verbatim, so a result maps back with no lookup table.</para>
/// </summary>
public sealed class MatchRequestBuilder : IMatchRequestBuilder
{
    /// <summary>
    /// The pessimistic per-item output-token ceiling the estimate prices against (match-schema §Cost model:
    /// a match record — score, band, up to ten missing skills, an optional salary and up to five reasons — is
    /// larger than an enrichment). Deliberately over-stated so the cost ceiling errs toward under-spending
    /// (ADR-F3-0002 / invariant 6).
    /// </summary>
    public const int MaxOutputTokensPerItem = 550;

    public MatchBatchRequest Build(
        IReadOnlyList<MatchJobContent> jobs,
        Profile profile,
        CvVersion cvVersion)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(cvVersion);

        var schema = MatchSchema.Build();
        var items = new List<BatchRequestItem>(jobs.Count);

        foreach (var job in jobs)
        {
            var rendered = MatchPrompt.Render(new MatchPromptInput(
                // --- Candidate: the one boundary the CV crosses (stable across the batch) ---
                CvText: cvVersion.ExtractedText,
                SalaryFloor: profile.SalaryFloor,
                SalaryFloorCurrency: profile.SalaryFloorCurrency,
                OwnerTimezoneBand: profile.TimezoneBand.ToString(),
                EmploymentTypesOpenTo: FormatEmploymentTypes(profile),
                // --- Role: per item ---
                CompanyName: job.CompanyName,
                Title: job.Title,
                Seniority: job.Seniority,
                LocationSummary: job.LocationSummary,
                EmploymentType: job.EmploymentType,
                PublishedSalary: job.PublishedSalary,
                Description: job.Description,
                // --- Enrichment: null omits the enrichment lines entirely (AC-09) ---
                Enrichment: ToFacts(job.Enrichment)));

            // The candidate block is the shared cache prefix (byte-identical across the batch); the role block
            // is the per-item suffix. Carrying them as two fields — rather than one string glued with a marker —
            // lets the adapter place the real cache_control breakpoint at the prefix's end while the fallback
            // and the cost accountant still see one continuous user message via FullUserContent (T13).
            items.Add(new BatchRequestItem(
                CustomId: job.JobId.ToString(),
                SystemPrompt: MatchPrompt.System,
                UserContent: rendered.RoleBlock,
                OutputSchema: schema,
                CachePrefix: rendered.CvBlock));
        }

        return new MatchBatchRequest(MatchPrompt.PromptVersion, items, MaxOutputTokensPerItem);
    }

    private static string FormatEmploymentTypes(Profile profile) =>
        profile.EmploymentTypes.Count == 0
            ? "any"
            : string.Join(", ", profile.EmploymentTypes.Select(t => t.ToString()));

    private static MatchEnrichmentFacts? ToFacts(MatchEnrichmentContent? enrichment)
    {
        if (enrichment is null)
        {
            return null;
        }

        var estimatedSalary = enrichment.EstimatedSalary is { } s
            ? string.Create(CultureInfo.InvariantCulture, $"{s.Currency} {s.Min}-{s.Max}/{s.Period}")
            : null;

        return new MatchEnrichmentFacts(
            CompanyStage: enrichment.CompanyStage.ToString(),
            IsRemote: enrichment.IsRemote,
            TimezoneBand: enrichment.TimezoneBand.ToString(),
            IsContractorFriendly: enrichment.IsContractorFriendly,
            EstimatedSalary: estimatedSalary,
            SalaryConfidence: enrichment.EstimatedSalary?.Confidence,
            Technologies: string.Join(", ", enrichment.Technologies),
            AiUsage: enrichment.AiUsage.ToString());
    }
}
