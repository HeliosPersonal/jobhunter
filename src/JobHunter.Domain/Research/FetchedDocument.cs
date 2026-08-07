namespace JobHunter.Domain.Research;

/// <summary>
/// One document a fetcher retrieved for a company, before any synthesis (SAD §5). It is the citation
/// authority in flight: <see cref="Url"/> is the exact URL fetched — what a later claim must match by set
/// membership — and <see cref="ObservedAt"/> is the fetch time a claim inherits as its date (AC-03). The
/// orchestrator stores it as a <see cref="ResearchSource"/> before the model ever sees it (SAD §4 S2), which
/// is what turns "did the model make this up" into a membership check rather than a judgement.
///
/// <para><see cref="Text"/> is the extracted plain text handed to the synthesiser; it may be empty (a page
/// with no extractable text is treated as no document upstream), but never null — the model is handed this
/// value. <see cref="Url"/> is required, because a document with no URL could never be cited.</para>
/// </summary>
public sealed record FetchedDocument
{
    public FetchedDocument(string url, string title, string text, DateTimeOffset observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(text);

        Url = url.Trim();
        Title = title.Trim();
        Text = text;
        ObservedAt = observedAt;
    }

    /// <summary>The exact URL fetched — what a claim's asserted source must match.</summary>
    public string Url { get; }

    /// <summary>The document title; may be blank, since some feeds omit it — presentation, not citation.</summary>
    public string Title { get; }

    /// <summary>The extracted plain text handed to the synthesiser; may be empty, never null.</summary>
    public string Text { get; }

    /// <summary>Fetch time, which becomes the observed date of every claim that cites this document (AC-03).</summary>
    public DateTimeOffset ObservedAt { get; }
}
