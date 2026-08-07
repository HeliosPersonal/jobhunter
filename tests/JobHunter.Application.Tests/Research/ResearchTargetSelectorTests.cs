using JobHunter.Application.Research;
using JobHunter.Domain.Research;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Research;

/// <summary>
/// T05 — target selection (SAD §6.1, AC-05, AC-06). From the companies behind a Run's top jobs the selector
/// picks at most five to research: those with no dossier, or a stale one. Staleness composes the domain
/// <see cref="Freshness"/> policy across the categories a dossier actually covered — so a dossier that
/// surfaced <see cref="ResearchCategory.News"/> or <see cref="ResearchCategory.Layoffs"/> ages out at seven
/// days while a firmographic-only one lasts thirty. On-demand requests are separate and additive: they never
/// take an automatic slot, a fresh dossier is not refetched to satisfy one, and a company is never queued
/// twice.
/// </summary>
public sealed class ResearchTargetSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 7, 0, 0, TimeSpan.Zero);

    private static ResearchCandidate Company(double score, DossierFreshness? dossier = null) =>
        new(Guid.NewGuid(), score, dossier);

    private static DossierFreshness Dossier(int ageDays, params ResearchCategory[] covered) =>
        new(Now.AddDays(-ageDays), covered);

    [Fact]
    public void A_company_with_no_dossier_is_an_automatic_target()
    {
        var company = Company(0.9);

        var targets = ResearchTargetSelector.Select([company], [], Now);

        targets.Automatic.ShouldBe([company.CompanyId]);
    }

    [Fact]
    public void A_company_with_a_fresh_firmographic_dossier_is_not_a_target()
    {
        var company = Company(0.9, Dossier(20, ResearchCategory.Funding, ResearchCategory.Stack));

        var targets = ResearchTargetSelector.Select([company], [], Now);

        targets.Automatic.ShouldBeEmpty();
    }

    [Fact]
    public void A_company_with_a_stale_firmographic_dossier_is_a_target()
    {
        var company = Company(0.9, Dossier(31, ResearchCategory.Funding));

        var targets = ResearchTargetSelector.Select([company], [], Now);

        targets.Automatic.ShouldBe([company.CompanyId]);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, false)]
    [InlineData(31, true)]
    public void Firmographic_freshness_boundary_is_thirty_days(int ageDays, bool expectedTarget)
    {
        var company = Company(0.9, Dossier(ageDays, ResearchCategory.Funding));

        var targets = ResearchTargetSelector.Select([company], [], Now);

        targets.Automatic.Contains(company.CompanyId).ShouldBe(expectedTarget);
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    public void A_dossier_covering_news_ages_out_at_seven_days(int ageDays, bool expectedTarget)
    {
        // The same dossier also covers Funding, which is still fresh — but News pulls the whole dossier's
        // refresh forward, because its value evaporates fastest.
        var company = Company(0.9, Dossier(ageDays, ResearchCategory.Funding, ResearchCategory.News));

        var targets = ResearchTargetSelector.Select([company], [], Now);

        targets.Automatic.Contains(company.CompanyId).ShouldBe(expectedTarget);
    }

    [Fact]
    public void A_dossier_covering_layoffs_ages_out_at_seven_days()
    {
        var stale = Company(0.9, Dossier(8, ResearchCategory.Layoffs));
        var fresh = Company(0.9, Dossier(6, ResearchCategory.Layoffs));

        ResearchTargetSelector.Select([stale], [], Now).Automatic.ShouldBe([stale.CompanyId]);
        ResearchTargetSelector.Select([fresh], [], Now).Automatic.ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_dossier_still_ages_out_at_the_default_thirty_days()
    {
        // A dossier that covered nothing (every category unavailable) is not volatile, so it lasts thirty days.
        var fresh = Company(0.9, Dossier(20));
        var stale = Company(0.9, Dossier(31));

        ResearchTargetSelector.Select([fresh], [], Now).Automatic.ShouldBeEmpty();
        ResearchTargetSelector.Select([stale], [], Now).Automatic.ShouldBe([stale.CompanyId]);
    }

    [Fact]
    public void At_most_five_automatic_targets_are_chosen_by_score()
    {
        var companies = new[]
        {
            Company(0.10), Company(0.90), Company(0.50), Company(0.70),
            Company(0.30), Company(0.99), Company(0.60),
        };

        var targets = ResearchTargetSelector.Select(companies, [], Now);

        targets.Automatic.Count.ShouldBe(5);
        // The five highest scores, in descending order: 0.99, 0.90, 0.70, 0.60, 0.50.
        targets.Automatic.ShouldBe(new[]
        {
            companies[5].CompanyId, companies[1].CompanyId, companies[3].CompanyId,
            companies[6].CompanyId, companies[2].CompanyId,
        });
    }

    [Fact]
    public void Fresh_companies_are_excluded_before_the_five_are_counted()
    {
        // Two high-scoring companies are fresh; they must not consume a slot the stale ones deserve.
        var freshTop = Company(0.99, Dossier(1, ResearchCategory.Funding));
        var freshSecond = Company(0.98, Dossier(1, ResearchCategory.Funding));
        var stale = new[] { Company(0.5), Company(0.4), Company(0.3), Company(0.2), Company(0.1) };

        var targets = ResearchTargetSelector.Select([freshTop, freshSecond, .. stale], [], Now);

        targets.Automatic.Count.ShouldBe(5);
        targets.Automatic.ShouldNotContain(freshTop.CompanyId);
        targets.Automatic.ShouldNotContain(freshSecond.CompanyId);
    }

    [Fact]
    public void On_demand_requests_are_returned_separately_and_do_not_displace_automatic_targets()
    {
        var automatic = new[] { Company(0.5), Company(0.4), Company(0.3), Company(0.2), Company(0.1), Company(0.05) };
        var requested = Guid.NewGuid();

        var targets = ResearchTargetSelector.Select(automatic, [requested], Now);

        // The automatic cap is untouched by the on-demand request.
        targets.Automatic.Count.ShouldBe(5);
        targets.OnDemand.ShouldBe([requested]);
    }

    [Fact]
    public void An_on_demand_company_already_chosen_automatically_is_not_queued_twice()
    {
        var company = Company(0.9);

        var targets = ResearchTargetSelector.Select([company], [company.CompanyId], Now);

        targets.Automatic.ShouldBe([company.CompanyId]);
        targets.OnDemand.ShouldBeEmpty();
    }

    [Fact]
    public void A_duplicate_on_demand_request_is_queued_once()
    {
        var requested = Guid.NewGuid();

        var targets = ResearchTargetSelector.Select([], [requested, requested], Now);

        targets.OnDemand.ShouldBe([requested]);
    }
}
