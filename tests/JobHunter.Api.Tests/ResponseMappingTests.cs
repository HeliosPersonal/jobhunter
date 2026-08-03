using JobHunter.Api.Endpoints;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The response mappers are hand-written and total (T05). This pins the absent-value branches: a job with
/// no company, no salary, no seniority, no posted-at and not closed maps to nulls rather than throwing or
/// inventing a value (QG-2 — nothing is fabricated).
/// </summary>
public sealed class ResponseMappingTests
{
    [Fact]
    public void A_minimal_job_with_no_company_maps_every_absent_field_to_null()
    {
        var job = new Job(
            id: Guid.NewGuid(),
            companyId: Guid.NewGuid(),
            originRawPostingId: Guid.NewGuid(),
            fingerprint: JobTestData.Fingerprint(),
            fingerprintVersion: 1,
            title: "Backend Engineer",
            normalisedTitle: "backend engineer",
            description: "A role.",
            applyUrl: "https://boards.example.com/apply/1",
            locations: LocationSet.Empty,
            remotePolicy: RemotePolicy.Unknown,
            employmentType: EmploymentType.Unknown,
            postedAtGranularity: PostedAtGranularity.Day,
            firstSeenAt: JobTestData.Seen,
            lastSeenAt: JobTestData.Seen);

        var detail = ResponseMapping.ToDetail(job, company: null);

        detail.Company.ShouldBeNull();
        detail.Salary.ShouldBeNull();
        detail.SalaryRaw.ShouldBeNull();
        detail.Seniority.ShouldBeNull();
        detail.PostedAt.ShouldBeNull();
        detail.ClosedAt.ShouldBeNull();
        detail.Score.ShouldBeNull();
        detail.Locations.ShouldBeEmpty();
        detail.Technologies.ShouldBeEmpty();
    }

    [Fact]
    public void A_closed_job_maps_its_closed_instant()
    {
        var job = JobTestData.Job(Guid.NewGuid(), Guid.NewGuid());
        job.Close(JobTestData.Seen.AddDays(5));

        var detail = ResponseMapping.ToDetail(job, JobTestData.Company(job.CompanyId));

        detail.Status.ShouldBe("Closed");
        detail.ClosedAt.ShouldBe(JobTestData.Seen.AddDays(5).ToUnixTimeSeconds());
    }
}
