using JobHunter.Application.Reporting;
using JobHunter.Domain.Reporting;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Reporting;

/// <summary>
/// T13: near-duplicate grouping at digest assembly (F5 SAD §6.1, ADR-F2-0001 "computed at digest assembly").
/// The grouper is a pure, deterministic function over the already-score-ordered selected candidates: two
/// candidates that are the <em>same real opening</em> — same company and same normalised title — collapse to
/// one representative (the highest-scored, the query's first), with the grouped-away job ids kept on it so
/// nothing is lost. The properties that carry the step: near-duplicates group into one card; the grouped-away
/// jobs remain queryable; distinct roles never merge (the F2 "zero false merges" floor, realised at display
/// time); the representative is the highest-scored; and the grouping is deterministic across runs.
/// </summary>
public sealed class NearDuplicateGrouperTests
{
    private static DigestCandidate Candidate(
        Guid company, string normalisedTitle, decimal score, Guid? jobId = null) =>
        new(
            jobId ?? Guid.CreateVersion7(), score, Suppressed: false, SuppressionReason: null,
            ["A reason"], SalaryUsd: null, ApplyUrl: "https://apply.example.com/x",
            CompanyId: company, NormalisedTitle: normalisedTitle);

    [Fact]
    public void An_empty_set_groups_to_nothing()
    {
        NearDuplicateGrouper.Group([]).ShouldBeEmpty();
    }

    [Fact]
    public void Two_same_company_same_title_candidates_group_into_one_representative()
    {
        var acme = Guid.CreateVersion7();
        var top = Candidate(acme, "staff backend engineer", 90m);
        var dup = Candidate(acme, "staff backend engineer", 80m);

        var grouped = NearDuplicateGrouper.Group([top, dup]);

        var group = grouped.ShouldHaveSingleItem();
        // The higher-scored is the representative (the query already ordered it first).
        group.Representative.JobId.ShouldBe(top.JobId);
        group.GroupedJobIds.ShouldBe([dup.JobId]);
    }

    [Fact]
    public void The_grouped_away_job_remains_queryable_on_the_representative()
    {
        var acme = Guid.CreateVersion7();
        var top = Candidate(acme, "senior sre", 88m);
        var dup = Candidate(acme, "senior sre", 70m);

        var group = NearDuplicateGrouper.Group([top, dup]).ShouldHaveSingleItem();

        // Grouped, never dropped: the near-duplicate is reachable through the representative.
        group.GroupedJobIds.ShouldContain(dup.JobId);
    }

    [Fact]
    public void Distinct_titles_at_the_same_company_are_not_merged()
    {
        var acme = Guid.CreateVersion7();
        var sre = Candidate(acme, "senior sre", 90m);
        var backend = Candidate(acme, "senior backend engineer", 85m);

        var grouped = NearDuplicateGrouper.Group([sre, backend]);

        // The conservative floor: two different roles at one company are two cards, never one.
        grouped.Count.ShouldBe(2);
        grouped.ShouldAllBe(g => g.GroupedJobIds.Count == 0);
    }

    [Fact]
    public void The_same_title_at_different_companies_is_not_merged()
    {
        var acme = Guid.CreateVersion7();
        var globex = Guid.CreateVersion7();
        var atAcme = Candidate(acme, "staff engineer", 90m);
        var atGlobex = Candidate(globex, "staff engineer", 85m);

        var grouped = NearDuplicateGrouper.Group([atAcme, atGlobex]);

        // Same title, different employer — two genuinely different openings.
        grouped.Count.ShouldBe(2);
    }

    [Fact]
    public void A_candidate_with_a_blank_normalised_title_never_groups()
    {
        var acme = Guid.CreateVersion7();
        var blankA = Candidate(acme, "", 90m);
        var blankB = Candidate(acme, "   ", 85m);

        var grouped = NearDuplicateGrouper.Group([blankA, blankB]);

        // A missing comparison title is not evidence of a duplicate — when in doubt, do not group (ADR-F2-0001).
        grouped.Count.ShouldBe(2);
        grouped.ShouldAllBe(g => g.GroupedJobIds.Count == 0);
    }

    [Fact]
    public void A_candidate_with_an_empty_company_never_groups()
    {
        var untitledCompany = Candidate(Guid.Empty, "staff engineer", 90m);
        var alsoUntitled = Candidate(Guid.Empty, "staff engineer", 85m);

        var grouped = NearDuplicateGrouper.Group([untitledCompany, alsoUntitled]);

        // No company is no evidence of the same employer — the conservative floor keeps them apart.
        grouped.Count.ShouldBe(2);
    }

    [Fact]
    public void Grouping_is_case_and_whitespace_insensitive_on_the_normalised_title()
    {
        var acme = Guid.CreateVersion7();
        var top = Candidate(acme, "staff backend engineer", 90m);
        var dup = Candidate(acme, "  Staff Backend Engineer  ", 80m);

        var grouped = NearDuplicateGrouper.Group([top, dup]);

        // The normaliser already lowercases, but the grouper trims and compares invariantly as a belt-and-braces
        // floor, so trivially different renderings of one normalised title still collapse.
        grouped.ShouldHaveSingleItem().GroupedJobIds.ShouldBe([dup.JobId]);
    }

    [Fact]
    public void Three_near_duplicates_all_collapse_onto_the_top_scored_representative()
    {
        var acme = Guid.CreateVersion7();
        var top = Candidate(acme, "principal engineer", 95m);
        var mid = Candidate(acme, "principal engineer", 90m);
        var low = Candidate(acme, "principal engineer", 70m);

        var group = NearDuplicateGrouper.Group([top, mid, low]).ShouldHaveSingleItem();

        group.Representative.JobId.ShouldBe(top.JobId);
        group.GroupedJobIds.ShouldBe([mid.JobId, low.JobId]);
    }

    [Fact]
    public void Grouping_preserves_the_representatives_input_order()
    {
        var acme = Guid.CreateVersion7();
        var globex = Guid.CreateVersion7();
        var acmeTop = Candidate(acme, "staff sre", 95m);
        var globexRole = Candidate(globex, "backend lead", 90m);
        var acmeDup = Candidate(acme, "staff sre", 60m);

        var grouped = NearDuplicateGrouper.Group([acmeTop, globexRole, acmeDup]);

        // Representatives keep the score order they arrived in; the duplicate folds into the first Acme card.
        grouped.Select(g => g.Representative.JobId).ShouldBe([acmeTop.JobId, globexRole.JobId]);
        grouped[0].GroupedJobIds.ShouldBe([acmeDup.JobId]);
        grouped[1].GroupedJobIds.ShouldBeEmpty();
    }

    [Fact]
    public void Grouping_is_deterministic_across_runs()
    {
        var acme = Guid.CreateVersion7();
        var globex = Guid.CreateVersion7();
        var input = new[]
        {
            Candidate(acme, "staff sre", 95m),
            Candidate(globex, "backend lead", 90m),
            Candidate(acme, "staff sre", 85m),
            Candidate(globex, "backend lead", 80m),
        };

        var first = NearDuplicateGrouper.Group(input);
        var second = NearDuplicateGrouper.Group(input);

        first.Select(g => (g.Representative.JobId, string.Join(",", g.GroupedJobIds)))
            .ShouldBe(second.Select(g => (g.Representative.JobId, string.Join(",", g.GroupedJobIds))));
    }
}
