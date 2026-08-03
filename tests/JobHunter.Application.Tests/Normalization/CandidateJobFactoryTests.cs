using JobHunter.Application.Deduplication;
using JobHunter.Application.Normalization;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Normalization;

/// <summary>
/// T04: the shared candidate-job core. It runs the title/location/remote/salary normalisers and the
/// fingerprint over an <see cref="ExtractedPosting"/>, registers the origin as the job's first alias so
/// every job has at least one (AC-08), and preserves the published title untouched while only the
/// never-displayed normalised form feeds the fingerprint (AC-05). A missing required field is a recorded
/// failure, never an exception (AC-04), and the whole thing is a pure function of its inputs (SAD S5).
/// </summary>
public sealed class CandidateJobFactoryTests
{
    private static readonly DateTimeOffset Seen = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private static NormalizationContext Context() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "acme.com", Seen, Seen);

    private static ExtractedPosting Posting() =>
        new()
        {
            Title = "Senior Platform Engineer (Remote)",
            ApplyUrl = "https://acme.com/apply/1",
            Description = "Build it.",
            LocationText = "Berlin, Germany",
        };

    [Fact]
    public void It_builds_a_job_preserving_the_published_title_and_registering_the_origin_alias()
    {
        var context = Context();
        var jobId = Guid.CreateVersion7();

        var result = CandidateJobFactory.Create(jobId, Posting(), context);

        result.IsSuccess.ShouldBeTrue();
        var job = result.Value;
        job.Id.ShouldBe(jobId);
        job.Title.ShouldBe("Senior Platform Engineer (Remote)");
        job.NormalisedTitle.ShouldNotBe(job.Title);
        job.Fingerprint.Value.Length.ShouldBe(64);
        job.FingerprintVersion.ShouldBe(FingerprintCalculator.Version);
        job.Aliases.ShouldHaveSingleItem().RawPostingId.ShouldBe(context.RawPostingId);
    }

    [Fact]
    public void A_missing_title_is_a_failure()
    {
        var result = CandidateJobFactory.Create(Guid.CreateVersion7(), Posting() with { Title = null }, Context());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CandidateJobFactory.MissingTitle.Code);
    }

    [Fact]
    public void A_missing_apply_url_is_a_failure()
    {
        var result = CandidateJobFactory.Create(Guid.CreateVersion7(), Posting() with { ApplyUrl = "  " }, Context());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CandidateJobFactory.MissingApplyUrl.Code);
    }

    [Fact]
    public void Structured_locations_win_over_free_text_when_the_provider_supplied_them()
    {
        var posting = Posting() with
        {
            Locations = LocationParser.FromParts("Netherlands", city: "Amsterdam"),
            LocationText = "ignored",
        };

        var result = CandidateJobFactory.Create(Guid.CreateVersion7(), posting, Context());

        result.Value.Locations.Locations.ShouldContain(l => l.Country == "Netherlands");
    }

    [Fact]
    public void The_same_inputs_produce_the_same_fingerprint()
    {
        var context = Context();

        var a = CandidateJobFactory.Create(Guid.CreateVersion7(), Posting(), context);
        var b = CandidateJobFactory.Create(Guid.CreateVersion7(), Posting(), context);

        a.Value.Fingerprint.ShouldBe(b.Value.Fingerprint);
    }
}
