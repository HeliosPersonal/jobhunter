using JobHunter.Application.Ratings;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Profiles;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Ratings;

/// <summary>
/// F4 T21 (ADR-F4-0003): the production regret matcher. It scores the sampled pre-match-excluded jobs at the
/// <em>cheap</em> tier through the same batch machinery a Run uses — building the request through
/// <see cref="IMatchRequestBuilder"/> (the one boundary the CV crosses), submitting at
/// <see cref="ModelTier.Cheap"/>, polling and draining the results, and parsing each with
/// <see cref="IMatchResultParser"/>. The would-be score it returns is the model's raw 0–100 fit judgement,
/// which is a conservative over-statement of the composed final score (ranking only multiplies it down), so a
/// falsification control errs toward over-alerting rather than hiding a regret.
/// </summary>
public sealed class RegretMatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeClock _clock = new(Now);
    private readonly SequentialIdGenerator _ids = new();
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly ICvVersionRepository _cvVersions = Substitute.For<ICvVersionRepository>();
    private readonly FakeMatchRequestBuilder _builder = new();
    private readonly FakeMatchResultParser _parser = new();

    [Fact]
    public async Task It_matches_the_sample_at_cheap_tier_and_returns_the_raw_fit_score()
    {
        var a = JobId(1);
        var b = JobId(2);
        _parser.Scores[a] = 72;
        _parser.Scores[b] = 18;
        WithActiveCv();
        var client = new FakeLlmBatchClient([Result(a), Result(b)]);
        var matcher = NewMatcher(client);

        var results = await matcher.MatchAsync([Content(a), Content(b)]);

        // The would-be score is the model's raw judgement — the sampler thresholds it, this only reports it.
        results.ShouldContain(m => m.JobId == a && m.WouldBeScore == 72m);
        results.ShouldContain(m => m.JobId == b && m.WouldBeScore == 18m);
        // The cheap tier is the whole economy of the control (ADR-F4-0003): a weekly sample must not cost deep money.
        client.LastSubmission!.Tier.ShouldBe(ModelTier.Cheap);
    }

    [Fact]
    public async Task The_cv_crosses_exactly_one_boundary_the_request_builder()
    {
        var job = JobId(1);
        _parser.Scores[job] = 50;
        var profile = WithActiveCv();
        var matcher = NewMatcher(new FakeLlmBatchClient([Result(job)]));

        await matcher.MatchAsync([Content(job)]);

        // The active Profile and CV reach the builder — the one place CV text is materialised — and nothing else.
        _builder.LastProfile.ShouldBe(profile);
        _builder.LastCvVersion.ShouldNotBeNull();
    }

    [Fact]
    public async Task An_empty_sample_returns_empty_without_submitting()
    {
        WithActiveCv();
        var client = new FakeLlmBatchClient();
        var matcher = NewMatcher(client);

        var results = await matcher.MatchAsync([]);

        results.ShouldBeEmpty();
        client.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task No_active_profile_returns_empty_without_submitting()
    {
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns((Profile?)null);
        var client = new FakeLlmBatchClient([Result(JobId(1))]);
        var matcher = NewMatcher(client);

        var results = await matcher.MatchAsync([Content(JobId(1))]);

        results.ShouldBeEmpty();
        client.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task No_active_cv_returns_empty_without_submitting()
    {
        var profile = ProfileFixture();
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(profile);
        _cvVersions.FindActiveAsync(profile.Id, Arg.Any<CancellationToken>()).Returns((CvVersion?)null);
        var client = new FakeLlmBatchClient([Result(JobId(1))]);
        var matcher = NewMatcher(client);

        var results = await matcher.MatchAsync([Content(JobId(1))]);

        results.ShouldBeEmpty();
        client.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_provider_error_or_unparseable_item_is_dropped_not_scored()
    {
        var good = JobId(1);
        var bad = JobId(2);
        _parser.Scores[good] = 60;   // 'bad' is deliberately absent from the parser's map → a parse failure
        WithActiveCv();
        var client = new FakeLlmBatchClient([Result(good), Result(bad)]);
        var matcher = NewMatcher(client);

        var results = await matcher.MatchAsync([Content(good), Content(bad)]);

        results.Count.ShouldBe(1);
        results[0].JobId.ShouldBe(good);
    }

    private RegretMatcher NewMatcher(FakeLlmBatchClient client) =>
        new(_profiles, _cvVersions, _builder, _parser, client, _clock, _ids,
            new RegretMatchingOptions(), NullLogger<RegretMatcher>.Instance);

    private Profile WithActiveCv()
    {
        var profile = ProfileFixture();
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(profile);
        _cvVersions.FindActiveAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(CvFixture(profile.Id));
        return profile;
    }

    private Profile ProfileFixture() =>
        new(_ids.NewId(), isActive: true, "Owner", 100000m, "USD", TimezoneBand.EMEA, [], [], Now);

    private CvVersion CvFixture(Guid profileId) =>
        new(_ids.NewId(), profileId, version: 1, isActive: true, "cv.pdf", "application/pdf", 1024,
            ContentHash.Compute("cv").Value, "CV text", Now, Now);

    private static Guid JobId(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static BatchResultItem Result(Guid jobId) =>
        new(jobId.ToString(), "{\"matchScore\":0}", null, TokenUsage.Zero);

    private static MatchJobContent Content(Guid jobId) =>
        new(jobId, "Acme", "acme.com", "Staff SRE", "Staff", "Remote", null, "FullTime", "desc", Enrichment: null);

    /// <summary>A builder that records the CV boundary it was handed and emits one item per job (custom_id = job id).</summary>
    private sealed class FakeMatchRequestBuilder : IMatchRequestBuilder
    {
        public Profile? LastProfile { get; private set; }

        public CvVersion? LastCvVersion { get; private set; }

        public MatchBatchRequest Build(IReadOnlyList<MatchJobContent> jobs, Profile profile, CvVersion cvVersion)
        {
            LastProfile = profile;
            LastCvVersion = cvVersion;
            var schema = new JsonSchema("record_match", "{}");
            var items = jobs
                .Select(j => new BatchRequestItem(j.JobId.ToString(), "system", "role", schema))
                .ToList();
            return new MatchBatchRequest("match-v1", items, 550);
        }
    }

    /// <summary>A parser that scores by job id and fails any item it has no score for (mirrors a real parse failure).</summary>
    private sealed class FakeMatchResultParser : IMatchResultParser
    {
        public Dictionary<Guid, int> Scores { get; } = new();

        public MatchParseOutcome Parse(MatchParseRequest request)
        {
            if (!Scores.TryGetValue(request.JobId, out var score))
            {
                return MatchParseOutcome.Failure("no score");
            }

            var match = new Match(
                request.MatchId, request.JobId, request.RunId, request.ProfileId, request.CvVersionId,
                score, InterviewProbability.Moderate, [], null, ["fit"], request.PromptVersion, request.CreatedAt);
            return MatchParseOutcome.Success(match);
        }
    }
}
