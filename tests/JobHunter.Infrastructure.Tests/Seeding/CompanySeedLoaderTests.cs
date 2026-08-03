using JobHunter.Domain.Companies;
using JobHunter.Infrastructure.Seeding;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Seeding;

/// <summary>
/// T03: the curated-seed loader is strict and positional. A well-formed sequence parses to typed entries;
/// a missing field, an unknown ATS kind, an uncanonicalisable domain or a duplicate fails the whole load
/// with a message that names the offending 1-based line, so a bad edit in a 300-entry file is fixed by
/// jumping to it rather than by bisecting. Parsing is pure — no database, no network.
/// </summary>
public sealed class CompanySeedLoaderTests
{
    [Fact]
    public void Parses_a_well_formed_entry_with_all_fields()
    {
        const string yaml = """
            - domain: stripe.com
              display_name: Stripe
              ats_kind: Greenhouse
              board_token: stripe
              careers_url: https://stripe.com/jobs
              hq_country: US
            """;

        var entries = CompanySeedLoader.Parse(yaml);

        entries.Count.ShouldBe(1);
        var entry = entries[0];
        entry.Domain.ShouldBe("stripe.com");
        entry.DisplayName.ShouldBe("Stripe");
        entry.AtsKind.ShouldBe(AtsKind.Greenhouse);
        entry.BoardToken.ShouldBe("stripe");
        entry.CareersUrl.ShouldBe("https://stripe.com/jobs");
        entry.HqCountry.ShouldBe("US");
    }

    [Fact]
    public void Parses_an_entry_without_the_optional_fields()
    {
        const string yaml = """
            - domain: plaid.com
              display_name: Plaid
              ats_kind: Lever
              board_token: plaid
            """;

        var entry = CompanySeedLoader.Parse(yaml).ShouldHaveSingleItem();

        entry.CareersUrl.ShouldBeNull();
        entry.HqCountry.ShouldBeNull();
        entry.CompBand.ShouldBeNull();
        entry.RemoteEmeaFriendly.ShouldBeNull();
    }

    [Fact]
    public void Parses_the_comp_band_and_remote_emea_segmentation_fields()
    {
        const string yaml = """
            - domain: stripe.com
              display_name: Stripe
              ats_kind: Greenhouse
              board_token: stripe
              comp_band: Top
              remote_emea_friendly: true
            """;

        var entry = CompanySeedLoader.Parse(yaml).ShouldHaveSingleItem();

        entry.CompBand.ShouldBe(CompBand.Top);
        entry.RemoteEmeaFriendly.ShouldBe(true);
    }

    [Fact]
    public void An_unknown_comp_band_fails_naming_the_line_and_the_allowed_values()
    {
        const string yaml = """
            - domain: stripe.com
              display_name: Stripe
              ats_kind: Greenhouse
              board_token: stripe
              comp_band: Platinum
            """;

        var ex = Should.Throw<CompanySeedException>(() => CompanySeedLoader.Parse(yaml));
        ex.Message.ShouldContain("line 1");
        ex.Message.ShouldContain("Platinum");
        ex.Message.ShouldContain("Top");
    }

    [Fact]
    public void A_non_boolean_remote_emea_friendly_fails_naming_the_line()
    {
        const string yaml = """
            - domain: stripe.com
              display_name: Stripe
              ats_kind: Greenhouse
              board_token: stripe
              remote_emea_friendly: maybe
            """;

        var ex = Should.Throw<CompanySeedException>(() => CompanySeedLoader.Parse(yaml));
        ex.Message.ShouldContain("line 1");
        ex.Message.ShouldContain("remote_emea_friendly");
    }

    [Fact]
    public void Parses_multiple_entries()
    {
        const string yaml = """
            - domain: stripe.com
              display_name: Stripe
              ats_kind: Greenhouse
              board_token: stripe
            - domain: ramp.com
              display_name: Ramp
              ats_kind: Ashby
              board_token: ramp
            """;

        CompanySeedLoader.Parse(yaml).Count.ShouldBe(2);
    }

    [Fact]
    public void An_empty_document_parses_to_no_entries()
    {
        CompanySeedLoader.Parse("").ShouldBeEmpty();
        CompanySeedLoader.Parse("# only a comment\n").ShouldBeEmpty();
    }

    [Fact]
    public void A_missing_required_field_fails_naming_its_line()
    {
        // display_name is absent; the entry mapping starts at line 1.
        const string yaml = """
            - domain: stripe.com
              ats_kind: Greenhouse
              board_token: stripe
            """;

        var ex = Should.Throw<CompanySeedException>(() => CompanySeedLoader.Parse(yaml));
        ex.Message.ShouldContain("line 1");
        ex.Message.ShouldContain("display_name");
    }

    [Fact]
    public void The_reported_line_points_at_the_offending_entry_not_the_first()
    {
        // The second entry (board_token missing) begins at line 5.
        const string yaml = """
            - domain: stripe.com
              display_name: Stripe
              ats_kind: Greenhouse
              board_token: stripe
            - domain: ramp.com
              display_name: Ramp
              ats_kind: Ashby
            """;

        var ex = Should.Throw<CompanySeedException>(() => CompanySeedLoader.Parse(yaml));
        ex.Message.ShouldContain("line 5");
        ex.Message.ShouldContain("board_token");
    }

    [Fact]
    public void An_unknown_ats_kind_fails_naming_the_line_and_the_allowed_values()
    {
        const string yaml = """
            - domain: stripe.com
              display_name: Stripe
              ats_kind: Bamboo
              board_token: stripe
            """;

        var ex = Should.Throw<CompanySeedException>(() => CompanySeedLoader.Parse(yaml));
        ex.Message.ShouldContain("line 1");
        ex.Message.ShouldContain("Bamboo");
        ex.Message.ShouldContain("Greenhouse");
    }

    [Fact]
    public void An_uncanonicalisable_domain_fails_naming_the_line()
    {
        const string yaml = """
            - domain: not-a-domain
              display_name: Bad
              ats_kind: Greenhouse
              board_token: bad
            """;

        var ex = Should.Throw<CompanySeedException>(() => CompanySeedLoader.Parse(yaml));
        ex.Message.ShouldContain("line 1");
        ex.Message.ShouldContain("not-a-domain");
    }

    [Fact]
    public void A_duplicate_domain_fails_naming_the_repeat_line()
    {
        const string yaml = """
            - domain: stripe.com
              display_name: Stripe
              ats_kind: Greenhouse
              board_token: stripe
            - domain: stripe.com
              display_name: Stripe Again
              ats_kind: Lever
              board_token: stripe2
            """;

        var ex = Should.Throw<CompanySeedException>(() => CompanySeedLoader.Parse(yaml));
        ex.Message.ShouldContain("line 5");
        ex.Message.ShouldContain("stripe.com");
    }

    [Fact]
    public void A_top_level_scalar_instead_of_a_sequence_fails()
    {
        var ex = Should.Throw<CompanySeedException>(() => CompanySeedLoader.Parse("just a string"));
        ex.Message.ShouldContain("sequence");
    }

    [Fact]
    public void A_non_mapping_sequence_item_fails()
    {
        const string yaml = """
            - just-a-scalar
            """;

        var ex = Should.Throw<CompanySeedException>(() => CompanySeedLoader.Parse(yaml));
        ex.Message.ShouldContain("not a mapping");
    }

    [Fact]
    public void Malformed_yaml_fails_as_a_seed_exception_not_a_raw_parse_error()
    {
        // Unbalanced flow mapping — a YAML syntax error, surfaced as an operator-facing message.
        const string yaml = "- { domain: stripe.com";

        var ex = Should.Throw<CompanySeedException>(() => CompanySeedLoader.Parse(yaml));
        ex.Message.ShouldContain("not valid YAML");
    }

    [Fact]
    public void Parse_rejects_a_null_argument()
    {
        Should.Throw<ArgumentNullException>(() => CompanySeedLoader.Parse(null!));
    }

    [Fact]
    public void The_shipped_seed_file_loads_and_every_entry_is_curatable()
    {
        // The real tools/seed/companies.yaml must always parse — a broken seed would fail the deploy-time
        // `seed` command, not a unit test, so we guard it here where it is cheap.
        var path = Path.Combine(RepositoryRoot(), "tools", "seed", "companies.yaml");
        var yaml = File.ReadAllText(path);

        var entries = CompanySeedLoader.Parse(yaml);

        entries.ShouldNotBeEmpty();
        entries.Select(e => e.Domain).ShouldBeUnique();
        foreach (var entry in entries)
        {
            CanonicalDomain.TryCreate(entry.Domain).IsSuccess.ShouldBeTrue();
            entry.BoardToken.ShouldNotBeNullOrWhiteSpace();
        }
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobHunter.slnx")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("Could not locate the repository root (JobHunter.slnx) from the test output directory.");
        return dir!.FullName;
    }
}
