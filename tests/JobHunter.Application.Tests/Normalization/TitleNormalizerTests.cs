using System.Globalization;
using JobHunter.Application.Normalization;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Normalization;

public sealed class TitleNormalizerTests
{
    [Fact]
    public void A_blank_title_normalises_to_empty_with_no_seniority()
    {
        var result = TitleNormalizer.Normalize("   ");

        result.Value.ShouldBe(string.Empty);
        result.Seniority.ShouldBeNull();
    }

    [Fact]
    public void A_null_title_normalises_to_empty()
    {
        TitleNormalizer.Normalize(null).Value.ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData("Senior Backend Engineer")]
    [InlineData("Sr. Backend Engineer")]
    [InlineData("Snr Backend Engineer")]
    public void Prefix_seniority_abbreviations_canonicalise_to_the_same_title(string title)
    {
        var result = TitleNormalizer.Normalize(title);

        result.Value.ShouldBe("senior backend engineer");
        result.Seniority.ShouldBe(Seniority.Senior);
    }

    [Theory]
    [InlineData("Senior Backend Engineer")]
    [InlineData("Sr. Backend Engineer")]
    [InlineData("Snr Backend Engineer")]
    [InlineData("Backend Engineer III")]
    public void All_forms_extract_the_same_seniority_level(string title)
    {
        // The extracted level is the same for every spelling (AC-05); the normalised string may keep the
        // marker in place, so "Backend Engineer III" stays a distinct — never a merged — job. A false
        // split here is tolerated; a false merge is not.
        TitleNormalizer.Normalize(title).Seniority.ShouldBe(Seniority.Senior);
    }

    [Fact]
    public void A_trailing_level_marker_is_canonicalised_in_place()
    {
        TitleNormalizer.Normalize("Backend Engineer III").Value.ShouldBe("backend engineer senior");
    }

    [Fact]
    public void Bracketed_and_post_dash_decoration_normalise_identically()
    {
        var remote = TitleNormalizer.Normalize("Backend Engineer (Remote)");
        var emea = TitleNormalizer.Normalize("Backend Engineer - EMEA");

        remote.Value.ShouldBe("backend engineer");
        emea.Value.ShouldBe("backend engineer");
        remote.Value.ShouldBe(emea.Value);
    }

    [Fact]
    public void A_team_after_a_pipe_is_kept_so_the_distinction_is_not_lost()
    {
        var payments = TitleNormalizer.Normalize("Backend Engineer | Payments");
        var plain = TitleNormalizer.Normalize("Backend Engineer");

        payments.Value.ShouldBe("backend engineer payments");
        payments.Value.ShouldNotBe(plain.Value);
    }

    [Fact]
    public void Whitespace_is_collapsed()
    {
        TitleNormalizer.Normalize("  Backend    Engineer  ").Value.ShouldBe("backend engineer");
    }

    [Fact]
    public void Bracketed_decoration_mid_title_is_removed()
    {
        TitleNormalizer.Normalize("Backend [Contract] Engineer").Value.ShouldBe("backend engineer");
    }

    [Fact]
    public void An_unmatched_closing_bracket_does_not_swallow_the_title()
    {
        TitleNormalizer.Normalize("Backend Engineer)").Value.ShouldBe("backend engineer");
    }

    [Fact]
    public void A_hyphenated_word_is_not_treated_as_a_separator()
    {
        // No surrounding space, so "full-stack" is one token, not a decoration cut.
        TitleNormalizer.Normalize("Full-Stack Engineer").Value.ShouldBe("full-stack engineer");
    }

    [Theory]
    [InlineData("Junior Developer", Seniority.Junior, "junior developer")]
    [InlineData("Jr Developer", Seniority.Junior, "junior developer")]
    [InlineData("Graduate Developer", Seniority.Junior, "junior developer")]
    [InlineData("Staff Engineer", Seniority.Staff, "staff engineer")]
    [InlineData("Principal Engineer", Seniority.Principal, "principal engineer")]
    [InlineData("Engineering Manager", Seniority.Manager, "engineering manager")]
    [InlineData("Tech Lead", Seniority.Lead, "tech lead")]
    [InlineData("Developer II", Seniority.Mid, "developer mid")]
    [InlineData("Intermediate Developer", Seniority.Mid, "mid developer")]
    public void Levels_are_extracted_and_canonicalised(string title, Seniority level, string normalised)
    {
        var result = TitleNormalizer.Normalize(title);

        result.Seniority.ShouldBe(level);
        result.Value.ShouldBe(normalised);
    }

    [Fact]
    public void A_title_with_no_level_marker_has_no_seniority()
    {
        TitleNormalizer.Normalize("Backend Engineer").Seniority.ShouldBeNull();
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void Normalisation_is_culture_invariant(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            // "ISTANBUL" lower-cased under tr-TR would keep a dotted-I; invariant casing must not.
            var result = TitleNormalizer.Normalize("SENIOR ISTANBUL Engineer");

            result.Value.ShouldBe("senior istanbul engineer");
            result.Seniority.ShouldBe(Seniority.Senior);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Accuracy_on_the_labelled_title_set_meets_the_threshold()
    {
        var cases = LabelledTitles.Cases;
        cases.Length.ShouldBeGreaterThanOrEqualTo(20);

        var correct = cases.Count(c =>
        {
            var result = TitleNormalizer.Normalize(c.Title);
            return result.Value == c.ExpectedNormalised && result.Seniority == c.ExpectedSeniority;
        });

        var accuracy = (double)correct / cases.Length;
        accuracy.ShouldBeGreaterThanOrEqualTo(0.95);
    }
}
