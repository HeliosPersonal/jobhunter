using System.Globalization;
using JobHunter.Domain.Companies;

namespace JobHunter.Scrapers.Tests.Detection;

/// <summary>
/// Synthesises the minimal board body each provider returns for a probe, with apply URLs that either do
/// or do not point back at the company domain — the strong signal in the detection table. Kept tiny (one
/// posting) because a probe only samples the head of a board.
/// </summary>
internal static class ProbeBoards
{
    public static string For(AtsKind kind, string domain, bool applyUrlMatchesDomain)
    {
        var applyHost = applyUrlMatchesDomain ? domain : "third-party-ats.example";
        return kind switch
        {
            AtsKind.Greenhouse =>
                $$"""{"jobs":[{"id":1001,"title":"Engineer","absolute_url":"https://{{applyHost}}/careers/1001","content":"<p>Role</p>","updated_at":"2026-07-01T00:00:00Z"}]}""",
            AtsKind.Lever =>
                $$"""[{"id":"11111111-1111-4111-8111-111111111111","text":"Engineer","hostedUrl":"https://{{applyHost}}/jobs/eng","descriptionPlain":"Role"}]""",
            AtsKind.Ashby =>
                $$"""{"jobs":[{"id":"ashby-1","title":"Engineer","applyUrl":"https://{{applyHost}}/careers/eng","descriptionPlain":"Role","updatedAt":"2026-07-01T00:00:00Z"}]}""",
            AtsKind.Workable =>
                $$"""{"jobs":[{"shortcode":"ABCDE12345","title":"Engineer","application_url":"https://{{applyHost}}/j/ABCDE12345","description":"<p>Role</p>","published_on":"2026-07-01"}]}""",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No probe board for this kind."),
        };
    }

    /// <summary>The absolute board URL an adapter builds for <paramref name="kind"/> and <paramref name="token"/>.</summary>
    public static string UrlFor(AtsKind kind, string token) => kind switch
    {
        AtsKind.Greenhouse => $"https://boards-api.greenhouse.io/v1/boards/{token}/jobs?content=true",
        AtsKind.Lever => $"https://api.lever.co/v0/postings/{token}?mode=json",
        AtsKind.Ashby => $"https://api.ashbyhq.com/posting-api/job-board/{token}?includeCompensation=true",
        AtsKind.Workable => $"https://apply.workable.com/api/v1/widget/accounts/{token}?details=true",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No board URL for this kind."),
    };

    public static string CountToken(int index) => "tok" + index.ToString(CultureInfo.InvariantCulture);
}
