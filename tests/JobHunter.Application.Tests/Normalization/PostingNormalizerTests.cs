using JobHunter.Application.Normalization;
using JobHunter.Application.Normalization.Providers;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Normalization;

/// <summary>
/// T04, AC-01/AC-04: each provider normaliser extracts the canonical fields from a recorded payload shape
/// with zero network, and turns a missing required field or a malformed payload into a
/// <see cref="JobHunter.Domain.Common.Result{T}"/> failure rather than an exception (AC-04). Extraction is a
/// pure function of the payload — the purity fact is asserted here by running the same payload twice and
/// getting an identical result, with no clock, id-generator or I/O in reach of the normaliser at all.
/// </summary>
public sealed class PostingNormalizerTests
{
    [Fact]
    public void Greenhouse_extracts_title_apply_url_location_and_decodes_html_description()
    {
        var payload =
            """
            {
              "title": "Senior Platform Engineer",
              "absolute_url": "https://boards.greenhouse.io/acme/jobs/123",
              "location": { "name": "Berlin, Germany" },
              "content": "&lt;p&gt;Build &amp;amp; run the platform.&lt;/p&gt;"
            }
            """;

        var result = new GreenhousePostingNormalizer().Extract(payload);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Title.ShouldBe("Senior Platform Engineer");
        result.Value.ApplyUrl.ShouldBe("https://boards.greenhouse.io/acme/jobs/123");
        result.Value.LocationText.ShouldBe("Berlin, Germany");
        result.Value.Description.ShouldBe("Build & run the platform.");
    }

    [Fact]
    public void Lever_maps_text_hostedurl_workplace_type_and_commitment()
    {
        var payload =
            """
            {
              "text": "Backend Engineer",
              "hostedUrl": "https://jobs.lever.co/acme/abc",
              "descriptionPlain": "We use Go and Postgres.",
              "workplaceType": "remote",
              "categories": { "location": "Remote - EMEA", "commitment": "Full-time" }
            }
            """;

        var result = new LeverPostingNormalizer().Extract(payload);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Title.ShouldBe("Backend Engineer");
        result.Value.ApplyUrl.ShouldBe("https://jobs.lever.co/acme/abc");
        result.Value.Description.ShouldBe("We use Go and Postgres.");
        result.Value.RemoteSignal.ShouldBe(RemotePolicy.Remote);
        result.Value.LocationText.ShouldBe("Remote - EMEA");
        result.Value.EmploymentType.ShouldBe(EmploymentType.FullTime);
    }

    [Fact]
    public void Ashby_joins_secondary_locations_and_reads_remote_flag_and_compensation()
    {
        var payload =
            """
            {
              "title": "Staff Engineer",
              "applyUrl": "https://jobs.ashbyhq.com/acme/xyz",
              "descriptionPlain": "Lead the payments platform.",
              "location": "London",
              "secondaryLocations": ["Dublin", "Lisbon"],
              "isRemote": true,
              "employmentType": "FullTime",
              "compensation": { "compensationTierSummary": "£110K – £140K" },
              "publishedAt": "2026-07-15T09:30:00Z"
            }
            """;

        var result = new AshbyPostingNormalizer().Extract(payload);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Title.ShouldBe("Staff Engineer");
        result.Value.LocationText.ShouldBe("London;Dublin;Lisbon");
        result.Value.RemoteSignal.ShouldBe(RemotePolicy.Remote);
        result.Value.SalaryText.ShouldBe("£110K – £140K");
        result.Value.PostedAt.ShouldBe(new DateTimeOffset(2026, 7, 15, 9, 30, 0, TimeSpan.Zero));
        result.Value.PostedAtGranularity.ShouldBe(PostedAtGranularity.Exact);
    }

    [Fact]
    public void Workable_reads_structured_country_city_telecommuting_and_day_granular_date()
    {
        var payload =
            """
            {
              "title": "Data Engineer",
              "application_url": "https://apply.workable.com/acme/j/DEADBEEF",
              "description": "<p>ETL at scale.</p>",
              "country": "Netherlands",
              "city": "Amsterdam",
              "telecommuting": true,
              "published_on": "2026-07-20"
            }
            """;

        var result = new WorkablePostingNormalizer().Extract(payload);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Title.ShouldBe("Data Engineer");
        result.Value.Description.ShouldBe("ETL at scale.");
        result.Value.RemoteSignal.ShouldBe(RemotePolicy.Remote);
        result.Value.Locations.ShouldNotBeNull();
        result.Value.Locations!.IsEmpty.ShouldBeFalse();
        result.Value.PostedAt.ShouldBe(new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero));
        result.Value.PostedAtGranularity.ShouldBe(PostedAtGranularity.Day);
    }

    [Fact]
    public void CareersPage_reads_jsonld_address_salary_and_is_marked_tier2()
    {
        var payload =
            """
            {
              "@type": "JobPosting",
              "title": "Site Reliability Engineer",
              "url": "https://acme.com/careers/sre",
              "description": "<p>Keep it up.</p>",
              "jobLocationType": "TELECOMMUTE",
              "employmentType": "FULL_TIME",
              "jobLocation": {
                "address": {
                  "addressCountry": "US",
                  "addressRegion": "CA",
                  "addressLocality": "San Francisco"
                }
              },
              "baseSalary": {
                "currency": "USD",
                "value": { "minValue": 150000, "maxValue": 190000, "unitText": "YEAR" }
              },
              "datePosted": "2026-07-01"
            }
            """;

        var result = new CareersPagePostingNormalizer().Extract(payload);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Title.ShouldBe("Site Reliability Engineer");
        result.Value.IsTier2.ShouldBeTrue();
        result.Value.RemoteSignal.ShouldBe(RemotePolicy.Remote);
        result.Value.EmploymentType.ShouldBe(EmploymentType.FullTime);
        result.Value.SalaryText.ShouldBe("USD 150000 - 190000 per year");
        result.Value.PostedAt.ShouldBe(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [MemberData(nameof(AllNormalizers))]
    public void A_payload_missing_the_title_extracts_but_fails_the_candidate(IPostingNormalizer normalizer)
    {
        // The provider still extracts (a null title is a legal ExtractedPosting); the candidate factory is
        // where the missing required field becomes a recorded failure (AC-04) — never an exception.
        var result = normalizer.Extract("{}");

        result.IsSuccess.ShouldBeTrue();
        var candidate = CandidateJobFactory.Create(Guid.CreateVersion7(), result.Value, Context());
        candidate.IsFailure.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(AllNormalizers))]
    public void A_malformed_payload_is_a_failure_not_an_exception(IPostingNormalizer normalizer)
    {
        var result = normalizer.Extract("{ not json");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(JsonPostingNormalizer.MalformedPayload.Code);
    }

    [Fact]
    public void Extraction_is_a_pure_function_of_the_payload()
    {
        const string payload =
            """
            {
              "title": "Senior Platform Engineer",
              "absolute_url": "https://boards.greenhouse.io/acme/jobs/123",
              "location": { "name": "Berlin, Germany" },
              "content": "&lt;p&gt;Hello&lt;/p&gt;"
            }
            """;
        var normalizer = new GreenhousePostingNormalizer();

        var first = normalizer.Extract(payload);
        var second = normalizer.Extract(payload);

        // Same payload, same result — no clock, no randomness, no I/O influences extraction (SAD S5).
        first.Value.ShouldBe(second.Value);
    }

    public static TheoryData<IPostingNormalizer> AllNormalizers() =>
        new()
        {
            new GreenhousePostingNormalizer(),
            new LeverPostingNormalizer(),
            new AshbyPostingNormalizer(),
            new WorkablePostingNormalizer(),
            new CareersPagePostingNormalizer(),
        };

    private static NormalizationContext Context() =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "acme.com",
            new DateTimeOffset(2026, 8, 3, 6, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 3, 6, 0, 0, TimeSpan.Zero));
}
