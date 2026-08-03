using System.Net;
using System.Net.Http.Json;
using JobHunter.Domain.Jobs;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The job read endpoints end-to-end (T05): detail with its company and — until F4 merges — a null score;
/// the provenance aliases for inspecting a bad merge; and the cursor-paged recent-jobs list, whose cursor
/// is opaque, rejects a mangled cursor with a 400 and returns an empty page past the end. Each endpoint
/// requires <c>jobhunter:read</c>, and no response carries a CV-derived value, match reason or application
/// note (QG-2).
/// </summary>
public sealed class JobEndpointTests : IClassFixture<EndpointsHostFactory>
{
    private readonly EndpointsHostFactory _factory;

    public JobEndpointTests(EndpointsHostFactory factory) => _factory = factory;

    // --- Detail ------------------------------------------------------------------------------------

    [Fact]
    public async Task Job_detail_returns_the_job_with_its_company_and_a_null_score_until_f4_lands()
    {
        var companyId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        _factory.Jobs.FindAsync(jobId, Arg.Any<CancellationToken>()).Returns(JobTestData.Job(jobId, companyId));
        _factory.Companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns(JobTestData.Company(companyId));

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri($"/api/jobs/{jobId}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DetailDto>();
        body.ShouldNotBeNull();
        body.Title.ShouldBe("Staff Backend Engineer");
        body.Status.ShouldBe("Live");
        body.RemotePolicy.ShouldBe("Remote");
        body.EmploymentType.ShouldBe("FullTime");
        body.Seniority.ShouldBe("Staff");
        body.Company.ShouldNotBeNull();
        body.Company.Name.ShouldBe("Snowflake");
        body.Company.Domain.ShouldBe("snowflake.com");
        body.Salary.ShouldNotBeNull();
        body.Salary.Currency.ShouldBe("EUR");
        body.Technologies.Select(t => t.Technology).ShouldBe(["Kafka", "C#"], ignoreOrder: true);
        // Score is F4-owned; until it merges the job carries no ranking, modelled as null (never fabricated).
        body.Score.ShouldBeNull();
    }

    [Fact]
    public async Task Job_detail_response_carries_no_cv_or_match_fields()
    {
        var companyId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        _factory.Jobs.FindAsync(jobId, Arg.Any<CancellationToken>()).Returns(JobTestData.Job(jobId, companyId));
        _factory.Companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns(JobTestData.Company(companyId));

        using var client = _factory.OwnerClient();
        var raw = await client.GetStringAsync(new Uri($"/api/jobs/{jobId}", UriKind.Relative));

        foreach (var forbidden in new[] { "matchReason", "missingSkill", "applicationNote", "cv", "normalisedTitle" })
        {
            raw.ShouldNotContain(forbidden, Case.Insensitive);
        }
    }

    [Fact]
    public async Task Job_detail_for_an_unknown_id_is_a_404_problem()
    {
        var jobId = Guid.NewGuid();
        _factory.Jobs.FindAsync(jobId, Arg.Any<CancellationToken>()).Returns((Job?)null);

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri($"/api/jobs/{jobId}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Job_detail_requires_a_read_token()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri($"/api/jobs/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Aliases -----------------------------------------------------------------------------------

    [Fact]
    public async Task Aliases_show_which_raw_postings_merged_into_the_job()
    {
        var jobId = Guid.NewGuid();
        _factory.Jobs.FindAsync(jobId, Arg.Any<CancellationToken>()).Returns(JobTestData.Job(jobId, Guid.NewGuid()));

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri($"/api/jobs/{jobId}/aliases", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var aliases = await response.Content.ReadFromJsonAsync<List<AliasDto>>();
        aliases.ShouldNotBeNull();
        aliases.Count.ShouldBe(2);
        aliases.ShouldAllBe(a => a.RawPostingId != Guid.Empty && a.SourceId != Guid.Empty);
    }

    [Fact]
    public async Task Aliases_for_an_unknown_job_is_a_404()
    {
        var jobId = Guid.NewGuid();
        _factory.Jobs.FindAsync(jobId, Arg.Any<CancellationToken>()).Returns((Job?)null);

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri($"/api/jobs/{jobId}/aliases", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- List --------------------------------------------------------------------------------------

    [Fact]
    public async Task The_recent_jobs_list_pages_and_yields_an_opaque_next_cursor()
    {
        var jobs = Enumerable.Range(0, 5)
            .Select(i => JobTestData.LiveJob(Guid.NewGuid(), JobTestData.Seen.AddDays(-i)))
            .ToList();
        _factory.LiveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(jobs);

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/jobs?limit=2", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ListDto>();
        body.ShouldNotBeNull();
        body.Jobs.Count.ShouldBe(2);
        body.NextCursor.ShouldNotBeNull();
        body.NextCursor.ShouldNotContain("firstSeenAt");
    }

    [Fact]
    public async Task The_last_page_of_the_list_has_no_next_cursor()
    {
        var jobs = Enumerable.Range(0, 2)
            .Select(i => JobTestData.LiveJob(Guid.NewGuid(), JobTestData.Seen.AddDays(-i)))
            .ToList();
        _factory.LiveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(jobs);

        using var client = _factory.OwnerClient();
        var body = await client.GetFromJsonAsync<ListDto>(new Uri("/api/jobs?limit=20", UriKind.Relative));

        body.ShouldNotBeNull();
        body.Jobs.Count.ShouldBe(2);
        body.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task A_cursor_past_the_end_returns_an_empty_page_rather_than_an_error()
    {
        var jobs = new[] { JobTestData.LiveJob(Guid.NewGuid(), JobTestData.Seen) }.ToList();
        _factory.LiveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(jobs);

        // A cursor positioned before every job (an older instant) leaves nothing behind it.
        var pastEnd = JobHunter.Api.Endpoints.JobsCursor.Encode(
            JobTestData.Seen.AddYears(-1).ToUnixTimeSeconds(), Guid.Empty);

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri($"/api/jobs?cursor={pastEnd}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ListDto>();
        body.ShouldNotBeNull();
        body.Jobs.ShouldBeEmpty();
        body.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task A_non_positive_limit_falls_back_to_the_default_page_size()
    {
        // 25 jobs, one day apart, and an explicitly invalid limit of 0: the binder must ignore it and
        // apply the default page size of 20 rather than returning nothing or the whole window.
        var jobs = Enumerable.Range(0, 25)
            .Select(i => JobTestData.LiveJob(Guid.NewGuid(), JobTestData.Seen.AddDays(-i)))
            .ToList();
        _factory.LiveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(jobs);

        using var client = _factory.OwnerClient();
        var body = await client.GetFromJsonAsync<ListDto>(new Uri("/api/jobs?limit=0", UriKind.Relative));

        body.ShouldNotBeNull();
        body.Jobs.Count.ShouldBe(20);
        body.NextCursor.ShouldNotBeNull();
    }

    [Fact]
    public async Task Jobs_sharing_a_first_seen_instant_page_by_the_id_tie_break()
    {
        // Two jobs discovered at the very same instant: the keyset must break the tie on id (descending),
        // so a cursor positioned at that instant with the larger id leaves exactly the smaller id behind.
        var shared = JobTestData.Seen;
        var high = JobTestData.LiveJob(new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), shared);
        var low = JobTestData.LiveJob(new Guid("00000000-0000-0000-0000-000000000001"), shared);
        _factory.LiveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([high, low]);

        var cursor = JobHunter.Api.Endpoints.JobsCursor.Encode(shared.ToUnixTimeSeconds(), high.Id);

        using var client = _factory.OwnerClient();
        var body = await client.GetFromJsonAsync<ListDto>(new Uri($"/api/jobs?cursor={cursor}", UriKind.Relative));

        body.ShouldNotBeNull();
        body.Jobs.Count.ShouldBe(1);
        body.Jobs[0].Id.ShouldBe(low.Id);
    }

    [Fact]
    public async Task A_mangled_list_cursor_is_a_400()
    {
        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/jobs?cursor=not-a-valid-cursor", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_list_requires_a_read_token()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/jobs", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private sealed record DetailDto(
        Guid Id,
        string Title,
        string Status,
        string RemotePolicy,
        string EmploymentType,
        string? Seniority,
        CompanyDto? Company,
        SalaryDto? Salary,
        IReadOnlyList<TechDto> Technologies,
        double? Score);

    private sealed record CompanyDto(string Name, string Domain, string? Stage, string? HqCountry);

    private sealed record SalaryDto(decimal Min, decimal Max, string Currency, string Period);

    private sealed record TechDto(string Technology, string MatchedVia);

    private sealed record AliasDto(Guid RawPostingId, Guid SourceId, long FirstSeenAt, long LastSeenAt);

    private sealed record ListDto(IReadOnlyList<SummaryDto> Jobs, string? NextCursor);

    private sealed record SummaryDto(Guid Id, string Title, long FirstSeenAt, long LastSeenAt);
}
