using System.Text.Json;
using JobHunter.Domain.Search;
using JobHunter.Search;
using Shouldly;
using Xunit;

namespace JobHunter.Search.Tests;

/// <summary>
/// The wire serialisation is a hand-written second statement of the allowlist (QG-2): the JSON carries
/// exactly the document's fields and no more, an optional null is <em>omitted</em> rather than sent as
/// JSON null (Typesense's convention for "no value"), and the document id is the job id so an upsert is
/// idempotent (SAD §8).
/// </summary>
public sealed class TypesenseSerializationTests
{
    private static JobDocument FullDocument() => new(
        Id: "0192e8b7-0000-7000-8000-000000000001",
        Title: "Staff Backend Engineer",
        CompanyName: "Snowflake",
        CompanyDomain: "snowflake.com",
        Description: "Distributed systems in C# and .NET.",
        Technologies: ["C#", ".NET", "Kafka"],
        Countries: ["DE", "NL"],
        RemotePolicy: "Remote",
        Seniority: "Staff",
        EmploymentType: "FullTime",
        CompanyStage: "Public",
        AiUsage: "Heavy",
        SalaryMin: 180_000,
        SalaryMax: 220_000,
        SalaryCurrency: "USD",
        Score: 95.0,
        PostedAt: 1_719_532_800,
        FirstSeenAt: 1_719_820_800,
        Status: "Live",
        ApplicationStatus: "Applied");

    private static JobDocument MinimalDocument() => new(
        Id: "0192e8b7-0000-7000-8000-000000000002",
        Title: "Backend Engineer",
        CompanyName: "Acme",
        CompanyDomain: "acme.com",
        Description: "desc",
        Technologies: [],
        Countries: [],
        RemotePolicy: "Onsite",
        Seniority: null,
        EmploymentType: "FullTime",
        CompanyStage: null,
        AiUsage: null,
        SalaryMin: null,
        SalaryMax: null,
        SalaryCurrency: null,
        Score: 0,
        PostedAt: null,
        FirstSeenAt: 1_719_820_800,
        Status: "Live",
        ApplicationStatus: null);

    [Fact]
    public void A_full_document_serialises_every_field_and_only_those_fields()
    {
        using var doc = JsonDocument.Parse(TypesenseSerialization.DocumentJson(FullDocument()));

        var propertyNames = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        propertyNames.ShouldBe(JobDocument.FieldNameSet, ignoreOrder: true);
    }

    [Fact]
    public void The_document_id_is_the_job_id_so_the_upsert_is_idempotent()
    {
        using var doc = JsonDocument.Parse(TypesenseSerialization.DocumentJson(FullDocument()));

        doc.RootElement.GetProperty("id").GetString().ShouldBe("0192e8b7-0000-7000-8000-000000000001");
    }

    [Fact]
    public void An_optional_null_field_is_omitted_never_sent_as_json_null()
    {
        using var doc = JsonDocument.Parse(TypesenseSerialization.DocumentJson(MinimalDocument()));
        var propertyNames = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

        propertyNames.ShouldNotContain("seniority");
        propertyNames.ShouldNotContain("companyStage");
        propertyNames.ShouldNotContain("aiUsage");
        propertyNames.ShouldNotContain("salaryMin");
        propertyNames.ShouldNotContain("salaryMax");
        propertyNames.ShouldNotContain("salaryCurrency");
        propertyNames.ShouldNotContain("postedAt");
        propertyNames.ShouldNotContain("applicationStatus");

        // Required fields are always present, even at their zero value.
        propertyNames.ShouldContain("score");
        propertyNames.ShouldContain("firstSeenAt");
        propertyNames.ShouldContain("status");
    }

    [Fact]
    public void Technologies_serialise_as_an_array_preserving_the_hash_and_dot_tokens()
    {
        using var doc = JsonDocument.Parse(TypesenseSerialization.DocumentJson(FullDocument()));

        var technologies = doc.RootElement.GetProperty("technologies")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        technologies.ShouldBe(["C#", ".NET", "Kafka"]);
    }

    [Fact]
    public void A_batch_serialises_as_newline_delimited_json_one_document_per_line()
    {
        var jsonl = TypesenseSerialization.DocumentsJsonl([FullDocument(), MinimalDocument()]);

        var lines = jsonl.Split('\n');
        lines.Length.ShouldBe(2);
        using var first = JsonDocument.Parse(lines[0]);
        using var second = JsonDocument.Parse(lines[1]);
        first.RootElement.GetProperty("id").GetString().ShouldBe("0192e8b7-0000-7000-8000-000000000001");
        second.RootElement.GetProperty("id").GetString().ShouldBe("0192e8b7-0000-7000-8000-000000000002");
    }

    [Fact]
    public void A_full_document_round_trips_through_write_and_read_unchanged()
    {
        var original = FullDocument();
        using var doc = JsonDocument.Parse(TypesenseSerialization.DocumentJson(original));

        var read = TypesenseSerialization.ReadDocument(doc.RootElement);

        AssertEquivalent(read, original);
    }

    [Fact]
    public void A_minimal_document_round_trips_with_every_optional_read_back_as_null()
    {
        var original = MinimalDocument();
        using var doc = JsonDocument.Parse(TypesenseSerialization.DocumentJson(original));

        var read = TypesenseSerialization.ReadDocument(doc.RootElement);

        AssertEquivalent(read, original);
        read.Seniority.ShouldBeNull();
        read.SalaryMin.ShouldBeNull();
        read.PostedAt.ShouldBeNull();
        read.ApplicationStatus.ShouldBeNull();
    }

    // Record equality compares the list fields by reference; compare the collections by value and the
    // rest by the record's own equality (with the collections normalised away).
    private static void AssertEquivalent(JobDocument actual, JobDocument expected)
    {
        actual.Technologies.ShouldBe(expected.Technologies);
        actual.Countries.ShouldBe(expected.Countries);
        (actual with { Technologies = [], Countries = [] })
            .ShouldBe(expected with { Technologies = [], Countries = [] });
    }

    [Fact]
    public void Reading_an_object_that_omits_every_field_yields_the_absent_values()
    {
        // A defensive path: a document Typesense returns with nothing recognisable reads as empty/zero,
        // never a throw — the query path degrades to a value (QG-3).
        using var doc = JsonDocument.Parse("{}");

        var read = TypesenseSerialization.ReadDocument(doc.RootElement);

        read.Id.ShouldBe(string.Empty);
        read.Title.ShouldBe(string.Empty);
        read.Technologies.ShouldBeEmpty();
        read.Countries.ShouldBeEmpty();
        read.Score.ShouldBe(0d);
        read.FirstSeenAt.ShouldBe(0L);
        read.Seniority.ShouldBeNull();
    }

    [Fact]
    public void Reading_a_string_array_field_that_is_not_an_array_yields_an_empty_list()
    {
        // technologies present but the wrong shape (a scalar) — read as empty rather than throwing.
        using var doc = JsonDocument.Parse("""{ "id": "x", "technologies": "not-an-array" }""");

        var read = TypesenseSerialization.ReadDocument(doc.RootElement);

        read.Technologies.ShouldBeEmpty();
    }
}
