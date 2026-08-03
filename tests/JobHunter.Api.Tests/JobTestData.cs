using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;

namespace JobHunter.Api.Tests;

/// <summary>Builders for the domain aggregates the job endpoints read, kept out of the test bodies.</summary>
internal static class JobTestData
{
    public static readonly DateTimeOffset Seen = new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);

    public static Fingerprint Fingerprint() =>
        JobHunter.Domain.Jobs.Fingerprint.TryCreate(new string('a', 64)).Value;

    public static LocationSet Locations() =>
        LocationSet.Of([JobLocation.TryCreate("Germany", region: "Berlin", city: "Berlin").Value]);

    public static Job Job(Guid id, Guid companyId)
    {
        var salary = SalaryRange.TryCreate(150000m, 220000m, "EUR", SalaryPeriod.Year).Value;

        var job = new Job(
            id: id,
            companyId: companyId,
            originRawPostingId: Guid.NewGuid(),
            fingerprint: Fingerprint(),
            fingerprintVersion: 1,
            title: "Staff Backend Engineer",
            normalisedTitle: "staff backend engineer",
            description: "Work on distributed systems with Kafka.",
            applyUrl: "https://boards.example.com/apply/123",
            locations: Locations(),
            remotePolicy: RemotePolicy.Remote,
            employmentType: EmploymentType.FullTime,
            postedAtGranularity: PostedAtGranularity.Day,
            firstSeenAt: Seen,
            lastSeenAt: Seen.AddDays(2),
            seniority: Seniority.Staff,
            salary: salary,
            salaryRaw: "€150k–€220k",
            postedAt: Seen);

        job.RegisterAlias(Guid.NewGuid(), Guid.NewGuid(), Seen, Seen.AddDays(1));
        job.RegisterAlias(Guid.NewGuid(), Guid.NewGuid(), Seen.AddDays(1), Seen.AddDays(2));
        job.AddTechnology("Kafka", TechnologyMatch.Description);
        job.AddTechnology("C#", TechnologyMatch.Vocabulary);

        return job;
    }

    public static Company Company(Guid id) => new(
        id: id,
        canonicalDomain: CanonicalDomain.TryCreate("snowflake.com").Value,
        displayName: "Snowflake",
        source: CompanySource.Curated,
        firstSeenAt: Seen,
        hqCountry: "US");

    public static AtsBinding Binding(Guid companyId) => new(
        id: Guid.NewGuid(),
        companyId: companyId,
        atsKind: AtsKind.Greenhouse,
        boardToken: "snowflake",
        confidence: BindingConfidence.TryCreate(0.95m).Value,
        evidence: """{"detector":"curated-seed"}""",
        detectedAt: Seen);

    public static LiveJob LiveJob(Guid id, DateTimeOffset firstSeen) => new(
        Id: id,
        CompanyId: Guid.NewGuid(),
        Title: "Backend Engineer",
        Seniority: "Senior",
        RemotePolicy: "Remote",
        EmploymentType: "FullTime",
        ApplyUrl: "https://boards.example.com/apply/" + id,
        FirstSeenAt: firstSeen,
        LastSeenAt: firstSeen.AddDays(1));
}
