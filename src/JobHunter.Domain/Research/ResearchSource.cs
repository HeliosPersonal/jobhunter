using JobHunter.Domain.Common;

namespace JobHunter.Domain.Research;

/// <summary>
/// One document fetched while researching a company, stored <em>before</em> synthesis (data-model
/// §research_sources, SAD §4 S2). This is the citation authority: <see cref="Url"/> is the exact URL
/// fetched — what a claim must later match, by set membership rather than string similarity — and
/// <see cref="ObservedAt"/> is the fetch time a claim inherits as its date (AC-03).
///
/// <para>The extracted text itself is not retained on the aggregate — it is reproducible by re-fetching,
/// and keeping third-party page content buys nothing — so only its <see cref="TextLength"/> survives, for
/// diagnostics.</para>
/// </summary>
public sealed class ResearchSource : Entity
{
    public ResearchSource(
        Guid id,
        ResearchCategory category,
        string url,
        string title,
        int textLength,
        DateTimeOffset observedAt)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(title);

        if (textLength < 0)
        {
            throw new ArgumentException("A source's text length cannot be negative.", nameof(textLength));
        }

        Category = category;
        Url = url.Trim();
        Title = title.Trim();
        TextLength = textLength;
        ObservedAt = observedAt;
    }

    private ResearchSource()
    {
    }

    /// <summary>Which fetcher retrieved this document.</summary>
    public ResearchCategory Category { get; private set; }

    /// <summary>The exact URL fetched — what a claim's asserted source must match.</summary>
    public string Url { get; private set; } = null!;

    /// <summary>The document title; may be blank, since some feeds omit it — presentation, not citation.</summary>
    public string Title { get; private set; } = null!;

    /// <summary>Length of the extracted text, kept for diagnostics after the text itself is discarded.</summary>
    public int TextLength { get; private set; }

    /// <summary>Fetch time, which becomes the observed date of every claim that cites this source (AC-03).</summary>
    public DateTimeOffset ObservedAt { get; private set; }
}
