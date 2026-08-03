using JobHunter.Domain.Companies;

namespace JobHunter.Domain.Sources;

/// <summary>
/// Derives the greppable base endpoint URL stored on a <see cref="JobSource"/> from its provider and
/// board token (data-model §job_sources: "derived from kind + token, stored so it is greppable"). The
/// stored URL is metadata for operators, not the fetch path — an adapter builds its own request URL from
/// the binding — so it carries no query parameters. For a Tier-2 <see cref="AtsKind.CareersPage"/> the
/// token already is the full careers URL, so it is returned verbatim.
/// </summary>
public static class AtsEndpoint
{
    public static string For(AtsKind kind, string boardToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boardToken);

        var token = Uri.EscapeDataString(boardToken);
        return kind switch
        {
            AtsKind.Greenhouse => $"https://boards-api.greenhouse.io/v1/boards/{token}/jobs",
            AtsKind.Lever => $"https://api.lever.co/v0/postings/{token}",
            AtsKind.Ashby => $"https://api.ashbyhq.com/posting-api/job-board/{token}",
            AtsKind.Workable => $"https://apply.workable.com/api/v1/widget/accounts/{token}",
            AtsKind.CareersPage => boardToken,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown ATS kind."),
        };
    }
}
