using JobHunter.Application.Normalization;
using JobHunter.Application.Normalization.Providers;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Normalization;

/// <summary>
/// T04, AC-01/AC-04: the field-absence and format arms of the Tier-2 careers-page (schema.org JSON-LD)
/// normaliser, complementing the single happy-path fact in <see cref="PostingNormalizerTests"/>. A JSON-LD
/// node is the most irregular source shape there is — every optional part may be missing or the wrong kind —
/// so each arm is pinned by value: a missing <c>jobLocation</c>/<c>address</c> yields no location; a
/// half-present or non-numeric <c>baseSalary</c> yields no salary rather than a malformed string; the
/// <c>unitText</c> maps to the four periods the free-text SalaryParser understands; and <c>datePosted</c>
/// resolves to a date-granular value, an exact instant, or a null fallback that never throws. Extraction is a
/// pure function of the payload, so these are zero-network, zero-clock unit tests.
/// </summary>
public sealed class CareersPagePostingNormalizerTests
{
    private static readonly CareersPagePostingNormalizer Normalizer = new();

    private static ExtractedPosting Extract(string payload)
    {
        var result = Normalizer.Extract(payload);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    // ---- location: absent jobLocation, absent address, empty address --------------------------

    [Fact]
    public void No_jobLocation_node_yields_no_location()
    {
        var posting = Extract("""{ "title": "SRE", "url": "https://acme.com/careers/sre" }""");

        posting.Locations.ShouldBeNull();
    }

    [Fact]
    public void A_jobLocation_with_no_address_yields_no_location()
    {
        var posting = Extract(
            """
            { "title": "SRE", "url": "https://acme.com/careers/sre", "jobLocation": { "@type": "Place" } }
            """);

        posting.Locations.ShouldBeNull();
    }

    [Fact]
    public void An_address_with_no_recognisable_parts_yields_no_location()
    {
        // Every address field is absent, so LocationParser.FromParts returns an empty set — which the
        // normaliser collapses to null rather than surfacing an empty LocationSet.
        var posting = Extract(
            """
            {
              "title": "SRE",
              "url": "https://acme.com/careers/sre",
              "jobLocation": { "address": { "@type": "PostalAddress" } }
            }
            """);

        posting.Locations.ShouldBeNull();
    }

    // ---- baseSalary: absent, currency-only, value-only, both-bounds-null ----------------------

    [Fact]
    public void No_baseSalary_node_yields_no_salary_text()
    {
        var posting = Extract("""{ "title": "SRE", "url": "https://acme.com/careers/sre" }""");

        posting.SalaryText.ShouldBeNull();
    }

    [Fact]
    public void A_baseSalary_missing_its_currency_yields_no_salary_text()
    {
        var posting = Extract(
            """
            {
              "title": "SRE",
              "url": "https://acme.com/careers/sre",
              "baseSalary": { "value": { "minValue": 100000, "maxValue": 120000, "unitText": "YEAR" } }
            }
            """);

        posting.SalaryText.ShouldBeNull();
    }

    [Fact]
    public void A_baseSalary_missing_its_value_object_yields_no_salary_text()
    {
        var posting = Extract(
            """
            { "title": "SRE", "url": "https://acme.com/careers/sre", "baseSalary": { "currency": "USD" } }
            """);

        posting.SalaryText.ShouldBeNull();
    }

    [Fact]
    public void A_baseSalary_whose_min_and_max_are_both_absent_yields_no_salary_text()
    {
        var posting = Extract(
            """
            {
              "title": "SRE",
              "url": "https://acme.com/careers/sre",
              "baseSalary": { "currency": "USD", "value": { "unitText": "YEAR" } }
            }
            """);

        posting.SalaryText.ShouldBeNull();
    }

    // ---- baseSalary amount shape: single bound, non-numeric bound -----------------------------

    [Fact]
    public void A_single_bound_salary_renders_just_that_amount()
    {
        // Only minValue present → the "{min ?? max}" single-amount branch, not the "min - max" range branch.
        var posting = Extract(
            """
            {
              "title": "SRE",
              "url": "https://acme.com/careers/sre",
              "baseSalary": { "currency": "EUR", "value": { "minValue": 90000, "unitText": "MONTH" } }
            }
            """);

        posting.SalaryText.ShouldBe("EUR 90000 per month");
    }

    [Fact]
    public void A_non_numeric_bound_is_ignored_and_the_numeric_one_still_renders()
    {
        // minValue is a string (ReadNumber's non-number guard → null); maxValue is a real number, so the
        // salary is not discarded — it renders the surviving numeric bound.
        var posting = Extract(
            """
            {
              "title": "SRE",
              "url": "https://acme.com/careers/sre",
              "baseSalary": { "currency": "USD", "value": { "minValue": "negotiable", "maxValue": 120000 } }
            }
            """);

        posting.SalaryText.ShouldBe("USD 120000 per year");
    }

    // ---- MapUnit: the four period arms plus the null default ----------------------------------

    [Theory]
    [InlineData("HOUR", "hour")]
    [InlineData("DAY", "day")]
    [InlineData("MONTH", "month")]
    [InlineData("YEAR", "year")]
    [InlineData("WEEK", "year")] // any unrecognised unit falls back to the yearly default
    public void The_unit_text_maps_to_the_period_the_salary_parser_understands(string unitText, string period)
    {
        var posting = Extract(
            $$"""
            {
              "title": "SRE",
              "url": "https://acme.com/careers/sre",
              "baseSalary": { "currency": "USD", "value": { "minValue": 100000, "maxValue": 120000, "unitText": "{{unitText}}" } }
            }
            """);

        posting.SalaryText.ShouldBe($"USD 100000 - 120000 per {period}");
    }

    [Fact]
    public void A_missing_unit_text_defaults_to_the_yearly_period()
    {
        var posting = Extract(
            """
            {
              "title": "SRE",
              "url": "https://acme.com/careers/sre",
              "baseSalary": { "currency": "USD", "value": { "minValue": 100000, "maxValue": 120000 } }
            }
            """);

        posting.SalaryText.ShouldBe("USD 100000 - 120000 per year");
    }

    // ---- datePosted: date-only, full instant, unparseable, absent -----------------------------

    [Fact]
    public void A_date_only_datePosted_is_day_granular()
    {
        var posting = Extract(
            """{ "title": "SRE", "url": "https://acme.com/careers/sre", "datePosted": "2026-07-01" }""");

        posting.PostedAt.ShouldBe(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        posting.PostedAtGranularity.ShouldBe(PostedAtGranularity.Day);
    }

    [Fact]
    public void A_full_instant_datePosted_is_exact_granular()
    {
        var posting = Extract(
            """{ "title": "SRE", "url": "https://acme.com/careers/sre", "datePosted": "2026-07-01T09:00:00Z" }""");

        posting.PostedAt.ShouldBe(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero));
        posting.PostedAtGranularity.ShouldBe(PostedAtGranularity.Exact);
    }

    [Fact]
    public void An_unparseable_datePosted_falls_back_to_no_date_at_day_granularity()
    {
        var posting = Extract(
            """{ "title": "SRE", "url": "https://acme.com/careers/sre", "datePosted": "sometime last spring" }""");

        posting.PostedAt.ShouldBeNull();
        posting.PostedAtGranularity.ShouldBe(PostedAtGranularity.Day);
    }

    [Fact]
    public void An_absent_datePosted_falls_back_to_no_date_at_day_granularity()
    {
        var posting = Extract("""{ "title": "SRE", "url": "https://acme.com/careers/sre" }""");

        posting.PostedAt.ShouldBeNull();
        posting.PostedAtGranularity.ShouldBe(PostedAtGranularity.Day);
    }

    // ---- jobLocationType: the non-telecommute arm ---------------------------------------------

    [Fact]
    public void A_non_telecommute_job_location_type_leaves_the_remote_signal_unset()
    {
        var posting = Extract(
            """
            { "title": "SRE", "url": "https://acme.com/careers/sre", "jobLocationType": "ONSITE" }
            """);

        posting.RemoteSignal.ShouldBeNull();
    }
}
