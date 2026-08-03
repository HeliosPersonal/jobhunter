using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Jobs;

public sealed class JobTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OriginPostingId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Seen = new(2026, 1, 1, 7, 0, 0, TimeSpan.Zero);

    private static Job NewJob(JobStatus status = JobStatus.Live) =>
        new(
            id: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            companyId: CompanyId,
            originRawPostingId: OriginPostingId,
            fingerprint: Fingerprint.TryCreate(new string('a', 64)).Value,
            fingerprintVersion: 1,
            title: "Senior Backend Engineer",
            normalisedTitle: "backend engineer",
            description: "Build things.",
            applyUrl: "https://example.com/apply",
            locations: LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            remotePolicy: RemotePolicy.Hybrid,
            employmentType: EmploymentType.FullTime,
            postedAtGranularity: PostedAtGranularity.Exact,
            firstSeenAt: Seen,
            lastSeenAt: Seen,
            seniority: Seniority.Senior,
            status: status);

    [Fact]
    public void A_new_job_is_live_with_its_published_title_preserved()
    {
        var job = NewJob();

        job.Status.ShouldBe(JobStatus.Live);
        job.Title.ShouldBe("Senior Backend Engineer");
        job.NormalisedTitle.ShouldBe("backend engineer");
        job.ClosedAt.ShouldBeNull();
        job.Seniority.ShouldBe(Seniority.Senior);
    }

    [Fact]
    public void An_empty_company_id_is_rejected()
    {
        Should.Throw<ArgumentException>(() => new Job(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Empty,
            OriginPostingId,
            Fingerprint.TryCreate(new string('a', 64)).Value,
            1,
            "Title",
            "title",
            "desc",
            "https://x",
            LocationSet.Empty,
            RemotePolicy.Remote,
            EmploymentType.FullTime,
            PostedAtGranularity.Exact,
            Seen,
            Seen));
    }

    [Fact]
    public void An_empty_origin_posting_id_is_rejected()
    {
        Should.Throw<ArgumentException>(() => new Job(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CompanyId,
            Guid.Empty,
            Fingerprint.TryCreate(new string('a', 64)).Value,
            1,
            "Title",
            "title",
            "desc",
            "https://x",
            LocationSet.Empty,
            RemotePolicy.Remote,
            EmploymentType.FullTime,
            PostedAtGranularity.Exact,
            Seen,
            Seen));
    }

    [Fact]
    public void A_blank_title_is_rejected()
    {
        Should.Throw<ArgumentException>(() => new Job(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CompanyId,
            OriginPostingId,
            Fingerprint.TryCreate(new string('a', 64)).Value,
            1,
            "   ",
            "title",
            "desc",
            "https://x",
            LocationSet.Empty,
            RemotePolicy.Remote,
            EmploymentType.FullTime,
            PostedAtGranularity.Exact,
            Seen,
            Seen));
    }

    [Fact]
    public void A_null_fingerprint_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new Job(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CompanyId,
            OriginPostingId,
            null!,
            1,
            "Title",
            "title",
            "desc",
            "https://x",
            LocationSet.Empty,
            RemotePolicy.Remote,
            EmploymentType.FullTime,
            PostedAtGranularity.Exact,
            Seen,
            Seen));
    }

    [Fact]
    public void Close_marks_the_job_closed_and_stamps_the_time()
    {
        var job = NewJob();
        var at = Seen.AddDays(30);

        var result = job.Close(at);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Closed);
        job.ClosedAt.ShouldBe(at);
    }

    [Fact]
    public void Close_is_idempotent_and_keeps_the_original_time()
    {
        var job = NewJob();
        var first = Seen.AddDays(30);
        job.Close(first);

        job.Close(Seen.AddDays(40)).IsSuccess.ShouldBeTrue();

        job.Status.ShouldBe(JobStatus.Closed);
        job.ClosedAt.ShouldBe(first);
    }

    [Fact]
    public void A_quarantined_job_cannot_be_closed()
    {
        var job = NewJob(JobStatus.Quarantined);

        var result = job.Close(Seen.AddDays(1));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Job.CannotCloseQuarantined);
        job.Status.ShouldBe(JobStatus.Quarantined);
    }

    [Fact]
    public void Reopen_clears_the_closed_time_and_bumps_liveness()
    {
        var job = NewJob();
        job.Close(Seen.AddDays(30));
        var reopenAt = Seen.AddDays(31);

        var result = job.Reopen(reopenAt);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Live);
        job.ClosedAt.ShouldBeNull();
        job.LastSeenAt.ShouldBe(reopenAt);
    }

    [Fact]
    public void Reopen_is_idempotent_on_a_live_job()
    {
        var job = NewJob();

        job.Reopen(Seen.AddDays(1)).IsSuccess.ShouldBeTrue();

        job.Status.ShouldBe(JobStatus.Live);
    }

    [Fact]
    public void A_quarantined_job_cannot_be_reopened()
    {
        var job = NewJob(JobStatus.Quarantined);

        var result = job.Reopen(Seen.AddDays(1));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Job.CannotReopenQuarantined);
        job.Status.ShouldBe(JobStatus.Quarantined);
    }

    [Fact]
    public void Quarantine_stops_the_job_from_any_state()
    {
        var job = NewJob();
        job.Close(Seen.AddDays(30));

        job.Quarantine();

        job.Status.ShouldBe(JobStatus.Quarantined);
    }

    [Fact]
    public void Registering_an_alias_records_provenance()
    {
        var job = NewJob();
        var postingId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var sourceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        var alias = job.RegisterAlias(postingId, sourceId, Seen, Seen);

        job.Aliases.ShouldHaveSingleItem();
        alias.JobId.ShouldBe(job.Id);
        alias.RawPostingId.ShouldBe(postingId);
        alias.SourceId.ShouldBe(sourceId);
    }

    [Fact]
    public void Registering_the_same_posting_twice_bumps_last_seen_not_the_count()
    {
        var job = NewJob();
        var postingId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var sourceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        job.RegisterAlias(postingId, sourceId, Seen, Seen);

        var later = Seen.AddDays(3);
        var alias = job.RegisterAlias(postingId, sourceId, Seen, later);

        job.Aliases.ShouldHaveSingleItem();
        alias.LastSeenAt.ShouldBe(later);
        job.LastSeenAt.ShouldBe(later);
    }

    [Fact]
    public void Registering_an_alias_never_moves_last_seen_backwards()
    {
        var job = NewJob();
        job.RegisterAlias(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Seen,
            Seen.AddDays(10));

        job.RegisterAlias(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Seen,
            Seen.AddDays(2));

        job.LastSeenAt.ShouldBe(Seen.AddDays(10));
    }

    [Fact]
    public void Adding_a_technology_records_it_once()
    {
        var job = NewJob();

        job.AddTechnology("C#", TechnologyMatch.Title);
        job.AddTechnology("C#", TechnologyMatch.Description);

        job.Technologies.ShouldHaveSingleItem();
        job.Technologies[0].Technology.ShouldBe("C#");
        job.Technologies[0].MatchedVia.ShouldBe(TechnologyMatch.Title);
    }

    [Fact]
    public void Adding_distinct_technologies_keeps_them_all()
    {
        var job = NewJob();

        job.AddTechnology("C#", TechnologyMatch.Title);
        job.AddTechnology("Go", TechnologyMatch.Description);

        job.Technologies.Select(t => t.Technology).ShouldBe(["C#", "Go"]);
    }
}
