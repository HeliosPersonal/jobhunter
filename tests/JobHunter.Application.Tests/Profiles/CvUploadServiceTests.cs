using System.Text;
using JobHunter.Application.Profiles;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Profiles;

/// <summary>
/// The CV upload service (T03): the whole write path for a CV. These tests hold the service to every
/// clause of the task's "Done when" without a database — the media type is sniffed not trusted, the size
/// cap is enforced <em>before</em> extraction, a text-less PDF is refused, identical content is a no-op,
/// and a new version deactivates the previous one atomically. The one rule that matters most is asserted
/// too: no CV text ever appears in the returned metadata, only the version identity.
/// </summary>
public sealed class CvUploadServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly ICvVersionRepository _cvVersions = Substitute.For<ICvVersionRepository>();
    private readonly ICvTextExtractor _extractor = Substitute.For<ICvTextExtractor>();
    private readonly IMatchRepository _matches = Substitute.For<IMatchRepository>();
    private readonly ILiveJobsQuery _liveJobs = Substitute.For<ILiveJobsQuery>();
    private readonly IReMatchBacklog _queue = Substitute.For<IReMatchBacklog>();
    private readonly SequentialIdGenerator _ids = new();
    private readonly FakeClock _clock = new(Now);

    private CvUploadService NewService()
    {
        _liveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Domain.Jobs.LiveJob>());
        _queue.EnqueueAsync(Arg.Any<Domain.Intelligence.ReMatchQueueItem>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var scheduler = new ReMatchScheduler(
            _matches, _liveJobs, _queue, new ReMatchOptions(), _ids, _clock,
            NullLogger<ReMatchScheduler>.Instance);
        return new CvUploadService(_profiles, _cvVersions, _extractor, scheduler, _ids, _clock);
    }

    private static Profile ActiveProfile() =>
        new(
            Guid.CreateVersion7(), isActive: true, "Owner", salaryFloor: null, salaryFloorCurrency: null,
            TimezoneBand.EMEA, preferredCountries: ["Germany"], employmentTypes: [EmploymentType.FullTime], Now);

    private void HasActiveProfile(Profile profile) =>
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(profile);

    private static byte[] Markdown(string text) => Encoding.UTF8.GetBytes(text);

    // --- Happy path -------------------------------------------------------------------------------

    [Fact]
    public async Task A_new_cv_is_extracted_versioned_and_activated()
    {
        var profile = ActiveProfile();
        HasActiveProfile(profile);
        _cvVersions.NextVersionAsync(profile.Id, Arg.Any<CancellationToken>()).Returns((short)3);
        _extractor.Extract(CvMediaType.Markdown, Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(Result<string>.Success("Senior platform engineer."));
        CvVersion? activated = null;
        await _cvVersions.ActivateAsync(Arg.Do<CvVersion>(v => activated = v), Arg.Any<CancellationToken>());

        var result = await NewService().UploadAsync("cv.md", Markdown("# CV\nSenior platform engineer."));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Created.ShouldBeTrue();
        result.Value.Version.ShouldBe((short)3);
        activated.ShouldNotBeNull();
        activated.ProfileId.ShouldBe(profile.Id);
        activated.Version.ShouldBe((short)3);
        activated.IsActive.ShouldBeTrue();
        activated.MediaType.ShouldBe("text/markdown");
        activated.ExtractedText.ShouldBe("Senior platform engineer.");
        activated.UploadedAt.ShouldBe(Now);
        await _cvVersions.Received(1).ActivateAsync(Arg.Any<CvVersion>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_upload_result_carries_no_cv_text()
    {
        var profile = ActiveProfile();
        HasActiveProfile(profile);
        _cvVersions.NextVersionAsync(profile.Id, Arg.Any<CancellationToken>()).Returns((short)1);
        const string secret = "SENTINEL-CV-CONTENT-must-not-leak";
        _extractor.Extract(CvMediaType.Markdown, Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(Result<string>.Success(secret));

        var result = await NewService().UploadAsync("cv.md", Markdown(secret));

        // The returned metadata is the version identity and nothing else — never the CV text.
        var serialised = System.Text.Json.JsonSerializer.Serialize(result.Value);
        serialised.ShouldNotContain("SENTINEL");
    }

    [Fact]
    public async Task Activating_a_new_version_re_stales_older_matches_and_queues_recent_jobs()
    {
        var profile = ActiveProfile();
        HasActiveProfile(profile);
        _cvVersions.NextVersionAsync(profile.Id, Arg.Any<CancellationToken>()).Returns((short)2);
        _extractor.Extract(CvMediaType.Markdown, Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(Result<string>.Success("Senior platform engineer."));
        CvVersion? activated = null;
        await _cvVersions.ActivateAsync(Arg.Do<CvVersion>(v => activated = v), Arg.Any<CancellationToken>());

        var result = await NewService().UploadAsync("cv.md", Markdown("# CV v2"));

        result.IsSuccess.ShouldBeTrue();
        activated.ShouldNotBeNull();
        // AC-08: matches from older versions are re-staled against the just-activated version — never deleted.
        await _matches.Received(1).MarkNotCurrentExceptCvVersionAsync(activated.Id, Arg.Any<CancellationToken>());
        // The recent-live-jobs re-match window is consulted (queueing itself is covered by the scheduler tests).
        await _liveJobs.Received(1).DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // --- Size cap before extraction ---------------------------------------------------------------

    [Fact]
    public async Task A_file_over_the_cap_is_refused_before_extraction()
    {
        HasActiveProfile(ActiveProfile());
        var oversize = new byte[CvUploadService.MaxSizeBytes + 1];
        oversize[0] = (byte)'%'; // even a would-be PDF header does not save it

        var result = await NewService().UploadAsync("cv.pdf", oversize);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CvUploadService.Errors.TooLarge);
        // The cap is a precondition: the extractor is never reached (security §5).
        _extractor.DidNotReceiveWithAnyArgs().Extract(default, default);
    }

    // --- Sniffed, not trusted ---------------------------------------------------------------------

    [Fact]
    public async Task A_zip_named_pdf_is_refused_as_an_unsupported_type()
    {
        HasActiveProfile(ActiveProfile());
        byte[] zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];

        var result = await NewService().UploadAsync("cv.pdf", zip);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CvUploadService.Errors.UnsupportedType);
        _extractor.DidNotReceiveWithAnyArgs().Extract(default, default);
    }

    // --- No OCR: a text-less PDF is refused with a clear message ----------------------------------

    [Fact]
    public async Task A_pdf_with_no_extractable_text_is_refused()
    {
        HasActiveProfile(ActiveProfile());
        _extractor.Extract(CvMediaType.Pdf, Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(Result<string>.Failure(new Error(CvUploadService.Errors.NoText, "A scan is not supported.")));

        var result = await NewService().UploadAsync("cv.pdf", Encoding.ASCII.GetBytes("%PDF-1.7\nscan"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CvUploadService.Errors.NoText);
        await _cvVersions.DidNotReceiveWithAnyArgs().ActivateAsync(default!, default);
    }

    // --- Content-hash no-op -----------------------------------------------------------------------

    [Fact]
    public async Task Identical_content_produces_no_new_version()
    {
        var profile = ActiveProfile();
        HasActiveProfile(profile);
        var content = Markdown("# CV\nSame bytes.");
        var existing = new CvVersion(
            Guid.CreateVersion7(), profile.Id, version: 2, isActive: true, "cv.md", "text/markdown",
            content.Length, new string('a', 64), "Same bytes.", Now, Now);
        _cvVersions.FindByHashAsync(profile.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await NewService().UploadAsync("cv.md", content);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Created.ShouldBeFalse();
        result.Value.CvVersionId.ShouldBe(existing.Id);
        result.Value.Version.ShouldBe((short)2);
        // No extraction, no insertion — the same CV is the same version.
        _extractor.DidNotReceiveWithAnyArgs().Extract(default, default);
        await _cvVersions.DidNotReceiveWithAnyArgs().ActivateAsync(default!, default);
        // T09 done-when: re-uploading identical content triggers no re-staling and no re-match.
        await _matches.DidNotReceiveWithAnyArgs().MarkNotCurrentExceptCvVersionAsync(default, default);
        await _liveJobs.DidNotReceiveWithAnyArgs().DiscoveredSinceAsync(default, default);
    }

    // --- Empty and no-profile guards --------------------------------------------------------------

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        HasActiveProfile(ActiveProfile());

        var result = await NewService().UploadAsync("cv.md", ReadOnlyMemory<byte>.Empty);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CvUploadService.Errors.Empty);
    }

    [Fact]
    public async Task An_upload_with_no_active_profile_is_refused()
    {
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns((Profile?)null);

        var result = await NewService().UploadAsync("cv.md", Markdown("# CV"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CvUploadService.Errors.NoActiveProfile);
    }
}
