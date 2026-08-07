using JobHunter.Telegram.Formatting;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The company dossier layout (F8 T09, AC-02/03/04/07). Every claim shows its observed date and links to its
/// source; warnings surface before every other category; a category that produced nothing is stated plainly
/// so absence is visible rather than ambiguous. Every dynamic value passes through the one MarkdownV2 escaper,
/// so a hostile title or a URL full of markup renders literally and can never break the send.
/// </summary>
public sealed class DossierFormatterTests
{
    private static readonly DateTimeOffset Observed = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Generated = new(2026, 8, 2, 6, 0, 0, TimeSpan.Zero);

    private static DossierClaim Claim(
        string category, string text, string url, bool isWarning = false, DateTimeOffset? observedAt = null) =>
        new(category, text, observedAt ?? Observed, url, isWarning);

    private static DossierView View(
        IReadOnlyList<DossierClaim> claims, IReadOnlyList<string>? unavailable = null, string summary = "A short honest summary.") =>
        new("Acme AI", summary, Generated, claims, unavailable ?? []);

    [Fact]
    public void It_shows_the_company_name_and_summary()
    {
        var rendered = DossierFormatter.Format(View([Claim("Funding", "Raised a Series B.", "https://acme.ai/press")]));

        rendered.ShouldContain("Acme AI");
        rendered.ShouldContain("A short honest summary");
    }

    [Fact]
    public void Every_claim_links_to_its_source_and_shows_its_observed_date()
    {
        var rendered = DossierFormatter.Format(View(
            [Claim("Funding", "Raised a Series B.", "https://acme.ai/press")]));

        // AC-02: the claim links to the exact fetched URL. AC-03: its observed date rides alongside.
        rendered.ShouldContain("(https://acme.ai/press)");
        rendered.ShouldContain("Raised a Series B");
        rendered.ShouldContain("1 Aug 2026");
    }

    [Fact]
    public void Warnings_are_rendered_before_every_other_category()
    {
        var rendered = DossierFormatter.Format(View(
        [
            Claim("Layoffs", "Cut 10% of staff.", "https://news.example/acme", isWarning: true),
            Claim("Funding", "Raised a Series B.", "https://acme.ai/press"),
        ]));

        // AC-04: the layoffs warning surfaces ahead of the funding claim regardless of input order.
        rendered.IndexOf("Cut 10% of staff", StringComparison.Ordinal)
            .ShouldBeLessThan(rendered.IndexOf("Raised a Series B", StringComparison.Ordinal));
    }

    [Fact]
    public void Unavailable_categories_are_stated_so_absence_is_visible()
    {
        var rendered = DossierFormatter.Format(View(
            [Claim("Funding", "Raised a Series B.", "https://acme.ai/press")],
            unavailable: ["Reviews", "InterviewProcess"]));

        // AC-07: absence of information is information — the categories that produced nothing are named.
        rendered.ShouldContain("Reviews");
        rendered.ShouldContain("InterviewProcess");
    }

    [Fact]
    public void A_dossier_with_no_claims_still_renders_its_summary_and_unavailable_categories()
    {
        var rendered = DossierFormatter.Format(View(
            [], unavailable: ["Funding", "Reviews"], summary: "Little could be found for this company."));

        rendered.ShouldContain("Little could be found");
        rendered.ShouldContain("Funding");
        rendered.ShouldContain("Reviews");
    }

    [Fact]
    public void A_hostile_claim_and_url_are_escaped_and_cannot_break_the_send()
    {
        var rendered = DossierFormatter.Format(View(
            [Claim("News", "Broke *everything* [really]", "https://x.example/a(b)_c")]));

        // The markup characters in the claim text are escaped rather than left active.
        rendered.ShouldContain(@"\*everything\*");
        rendered.ShouldNotContain("Broke *everything*");
    }

    [Fact]
    public void The_observed_date_is_taken_per_claim_not_from_the_dossier()
    {
        var older = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        var rendered = DossierFormatter.Format(View(
        [
            Claim("Funding", "Raised a Series B.", "https://acme.ai/press", observedAt: older),
        ]));

        rendered.ShouldContain("15 Jul 2026");
    }
}
