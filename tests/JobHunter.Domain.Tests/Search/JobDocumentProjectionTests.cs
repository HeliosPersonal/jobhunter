using JobHunter.Domain.Search;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Search;

/// <summary>
/// The projection is a pure function of its inputs (T01): given the same source it produces the same
/// document, with no clock and no infrastructure. These assertions also pin the two decoupling rules —
/// an absent score projects to 0, and the F3/F4/F6 fields pass through as null when absent.
/// </summary>
public sealed class JobDocumentProjectionTests
{
    private static readonly DateTimeOffset FirstSeen =
        new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Posted =
        new(2026, 6, 28, 0, 0, 0, TimeSpan.Zero);

    private static JobProjectionSource FullSource() => new()
    {
        Id = Guid.Parse("0192e8b7-0000-7000-8000-000000000001"),
        Title = "Staff Backend Engineer",
        Description = "Work on distributed systems.",
        Status = "Live",
        CompanyName = "Snowflake",
        CompanyDomain = "snowflake.com",
        CompanyStage = "Public",
        Technologies = ["Kafka", "Azure", "C#"],
        Countries = ["DE", "NL"],
        RemotePolicy = "Remote",
        Seniority = "Staff",
        EmploymentType = "FullTime",
        AiUsage = "Heavy",
        SalaryMin = 180_000,
        SalaryMax = 220_000,
        SalaryCurrency = "USD",
        Score = 95.0,
        PostedAt = Posted,
        FirstSeenAt = FirstSeen,
        ApplicationStatus = "Applied",
    };

    [Fact]
    public void Projects_every_field_from_the_source()
    {
        var document = JobDocumentProjection.ToDocument(FullSource());

        document.Id.ShouldBe("0192e8b7-0000-7000-8000-000000000001");
        document.Title.ShouldBe("Staff Backend Engineer");
        document.CompanyName.ShouldBe("Snowflake");
        document.CompanyDomain.ShouldBe("snowflake.com");
        document.Technologies.ShouldBe(["Kafka", "Azure", "C#"]);
        document.Countries.ShouldBe(["DE", "NL"]);
        document.RemotePolicy.ShouldBe("Remote");
        document.Seniority.ShouldBe("Staff");
        document.EmploymentType.ShouldBe("FullTime");
        document.CompanyStage.ShouldBe("Public");
        document.AiUsage.ShouldBe("Heavy");
        document.SalaryMin.ShouldBe(180_000);
        document.SalaryMax.ShouldBe(220_000);
        document.SalaryCurrency.ShouldBe("USD");
        document.Score.ShouldBe(95.0);
        document.PostedAt.ShouldBe(Posted.ToUnixTimeSeconds());
        document.FirstSeenAt.ShouldBe(FirstSeen.ToUnixTimeSeconds());
        document.Status.ShouldBe("Live");
        document.ApplicationStatus.ShouldBe("Applied");
    }

    [Fact]
    public void Is_deterministic_for_the_same_input()
    {
        var source = FullSource();

        JobDocumentProjection.ToDocument(source).ShouldBe(JobDocumentProjection.ToDocument(source));
    }

    [Fact]
    public void An_absent_score_projects_to_zero()
    {
        var source = FullSource() with { Score = null };

        JobDocumentProjection.ToDocument(source).Score.ShouldBe(0d);
    }

    [Fact]
    public void Absent_enrichment_score_and_application_fields_pass_through_as_null()
    {
        var source = new JobProjectionSource
        {
            Id = Guid.NewGuid(),
            Title = "Backend Engineer",
            Description = "desc",
            Status = "Live",
            CompanyName = "Acme",
            CompanyDomain = "acme.com",
            RemotePolicy = "Onsite",
            EmploymentType = "FullTime",
            FirstSeenAt = FirstSeen,
        };

        var document = JobDocumentProjection.ToDocument(source);

        document.CompanyStage.ShouldBeNull();
        document.AiUsage.ShouldBeNull();
        document.Seniority.ShouldBeNull();
        document.SalaryMin.ShouldBeNull();
        document.SalaryCurrency.ShouldBeNull();
        document.ApplicationStatus.ShouldBeNull();
        document.PostedAt.ShouldBeNull();
        document.Score.ShouldBe(0d);
        document.Technologies.ShouldBeEmpty();
        document.Countries.ShouldBeEmpty();
    }

    [Fact]
    public void Rejects_a_null_source()
    {
        Should.Throw<ArgumentNullException>(() => JobDocumentProjection.ToDocument(null!));
    }
}
