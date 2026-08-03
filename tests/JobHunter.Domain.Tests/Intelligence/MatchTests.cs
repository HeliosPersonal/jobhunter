using JobHunter.Domain.Intelligence;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Intelligence;

public sealed class MatchTests
{
    private static readonly Guid MatchId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-0000000000D1");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid ProfileId = Guid.Parse("00000000-0000-0000-0000-0000000000F1");
    private static readonly Guid CvVersionId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");

    private static Match NewMatch(
        int matchScore = 82,
        InterviewProbability probability = InterviewProbability.Good,
        IReadOnlyList<string>? missingSkills = null,
        SalaryExpectation? salary = null,
        IReadOnlyList<string>? reasons = null)
    {
        var clock = new FakeClock();
        return new Match(
            MatchId,
            JobId,
            RunId,
            ProfileId,
            CvVersionId,
            matchScore,
            probability,
            missingSkills ?? ["Rust"],
            salary,
            reasons ?? ["Strong platform-engineering overlap; the CV shows Go and Kubernetes at scale."],
            "match-v1",
            clock.UtcNow);
    }

    [Fact]
    public void A_valid_match_exposes_its_fields()
    {
        var salary = SalaryExpectation.TryCreate(100000m, 140000m, "EUR").Value;

        var match = NewMatch(salary: salary);

        match.JobId.ShouldBe(JobId);
        match.RunId.ShouldBe(RunId);
        match.ProfileId.ShouldBe(ProfileId);
        match.CvVersionId.ShouldBe(CvVersionId);
        match.MatchScore.ShouldBe(82);
        match.InterviewProbability.ShouldBe(InterviewProbability.Good);
        match.MissingSkills.ShouldBe(["Rust"]);
        match.SalaryExpectation.ShouldBe(salary);
        match.PromptVersion.ShouldBe("match-v1");
        match.IsCurrent.ShouldBeTrue();
        match.Reasons.Count.ShouldBe(1);
    }

    [Fact]
    public void An_empty_reasons_list_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewMatch(reasons: []));
    }

    [Fact]
    public void A_whitespace_only_reasons_list_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewMatch(reasons: ["", "  ", "\t"]));
    }

    [Fact]
    public void Blank_reasons_are_trimmed_out_but_a_real_one_survives()
    {
        var match = NewMatch(reasons: ["  ", "  Strong overlap.  ", ""]);

        match.Reasons.Count.ShouldBe(1);
        match.Reasons[0].ShouldBe("Strong overlap.");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void An_out_of_range_match_score_is_rejected(int score)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewMatch(matchScore: score));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void The_score_bounds_are_inclusive(int score)
    {
        var match = NewMatch(matchScore: score);

        match.MatchScore.ShouldBe(score);
    }

    [Fact]
    public void Missing_skills_may_be_empty()
    {
        var match = NewMatch(missingSkills: []);

        match.MissingSkills.ShouldBeEmpty();
    }

    [Fact]
    public void Missing_skills_are_trimmed_deblanked_and_capped_at_ten()
    {
        var many = Enumerable.Range(0, 20).Select(i => $"skill{i}").ToList();

        var match = NewMatch(missingSkills: many);

        match.MissingSkills.Count.ShouldBe(Match.MaxMissingSkills);
    }

    [Fact]
    public void A_null_salary_expectation_is_allowed()
    {
        var match = NewMatch(salary: null);

        match.SalaryExpectation.ShouldBeNull();
    }

    [Fact]
    public void Constructor_rejects_empty_reference_ids()
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentException>(() => new Match(
            MatchId, Guid.Empty, RunId, ProfileId, CvVersionId, 50, InterviewProbability.Low, [], null, ["r"], "match-v1", clock.UtcNow));
        Should.Throw<ArgumentException>(() => new Match(
            MatchId, JobId, Guid.Empty, ProfileId, CvVersionId, 50, InterviewProbability.Low, [], null, ["r"], "match-v1", clock.UtcNow));
        Should.Throw<ArgumentException>(() => new Match(
            MatchId, JobId, RunId, Guid.Empty, CvVersionId, 50, InterviewProbability.Low, [], null, ["r"], "match-v1", clock.UtcNow));
        Should.Throw<ArgumentException>(() => new Match(
            MatchId, JobId, RunId, ProfileId, Guid.Empty, 50, InterviewProbability.Low, [], null, ["r"], "match-v1", clock.UtcNow));
    }

    [Fact]
    public void Constructor_rejects_a_blank_prompt_version()
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentException>(() => new Match(
            MatchId, JobId, RunId, ProfileId, CvVersionId, 50, InterviewProbability.Low, [], null, ["r"], " ", clock.UtcNow));
    }

    [Fact]
    public void Mark_not_current_clears_the_flag_without_deleting()
    {
        var match = NewMatch();

        match.MarkNotCurrent();

        match.IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public void Interview_probability_is_a_band_not_a_number()
    {
        // SAD §11 D4: the four bands plus the parse-fallback sentinel, never a percentage.
        Enum.GetValues<InterviewProbability>().ShouldBe(
            [
                InterviewProbability.Low,
                InterviewProbability.Moderate,
                InterviewProbability.Good,
                InterviewProbability.Strong,
                InterviewProbability.Unknown,
            ]);
    }
}
