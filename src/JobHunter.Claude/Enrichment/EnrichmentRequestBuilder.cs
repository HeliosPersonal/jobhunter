using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;

namespace JobHunter.Claude.Enrichment;

/// <summary>
/// Renders the enrichment batch request from a Run's in-scope jobs (SAD §6.2, T10). It is the Claude-side
/// implementation of <see cref="IEnrichmentRequestBuilder"/>: the Application submit handler holds the
/// cost gate and the persistence, and delegates the one thing that must know the prompt and the schema —
/// building the items — to here, so the versioned prompt (<see cref="EnrichmentPrompt"/>), the generated
/// schema (<see cref="EnrichmentSchema"/>) and the prompt version stay in one place and the Application
/// layer stays free of any Anthropic concept (architecture rule 3).
///
/// <para>The rendering carries <strong>nothing about the Owner</strong>: an enrichment prompt describes
/// the job, not the fit, so the CV never enters this boundary (invariant — the CV crosses exactly one
/// boundary, F4's). <see cref="BatchRequestItem.CustomId"/> is the job id verbatim (SAD §8), so a result
/// maps back with no lookup table.</para>
/// </summary>
public sealed class EnrichmentRequestBuilder : IEnrichmentRequestBuilder
{
    /// <summary>
    /// The pessimistic per-item output-token ceiling the estimate prices against (contract §Cost model:
    /// ~350 output tokens each). Deliberately over-stated so the cost ceiling errs toward under-spending
    /// (ADR-F3-0002).
    /// </summary>
    public const int MaxOutputTokensPerItem = 350;

    public EnrichmentBatchRequest Build(IReadOnlyList<EnrichmentJobContent> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        var schema = EnrichmentSchema.Build();
        var items = new List<BatchRequestItem>(jobs.Count);

        foreach (var job in jobs)
        {
            var rendered = EnrichmentPrompt.RenderUser(new EnrichmentPromptInput(
                job.CompanyName,
                job.CanonicalDomain,
                job.Title,
                job.LocationSummary,
                job.PublishedSalary,
                job.EmploymentType,
                job.Description));

            items.Add(new BatchRequestItem(
                CustomId: job.JobId.ToString(),
                SystemPrompt: EnrichmentPrompt.System,
                UserContent: rendered.Content,
                OutputSchema: schema));
        }

        return new EnrichmentBatchRequest(EnrichmentPrompt.PromptVersion, items, MaxOutputTokensPerItem);
    }
}
