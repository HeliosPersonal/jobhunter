using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using JobHunter.Api.Endpoints;
using JobHunter.Domain.Common;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The CV upload endpoint end-to-end (T03, SAD §6.3): the one place a CV enters the system. It is
/// owner-scoped — a tokenless call is a 401 and a wrong-subject token is a 403 (AC-07) — the media type
/// is sniffed inside the service not trusted from the multipart headers, a text-less PDF is a 400, an
/// oversize file is a 413 refused before extraction, and identical content is a 200 no-op rather than a
/// spurious 201. No response body ever carries CV text (QG-2).
/// </summary>
public sealed class CvEndpointTests : IClassFixture<EndpointsHostFactory>
{
    private readonly EndpointsHostFactory _factory;

    public CvEndpointTests(EndpointsHostFactory factory)
    {
        _factory = factory;
        _factory.Profiles.ClearSubstitute();
        _factory.CvVersions.ClearSubstitute();
        _factory.CvTextExtractor.ClearSubstitute();
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private static Profile ActiveProfile() =>
        new(
            Guid.CreateVersion7(), isActive: true, "Owner", salaryFloor: null, salaryFloorCurrency: null,
            TimezoneBand.EMEA, preferredCountries: ["Germany"], employmentTypes: [EmploymentType.FullTime], Now);

    private void HasActiveProfile(Profile profile) =>
        _factory.Profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(profile);

    private static MultipartFormDataContent File(byte[] bytes, string fileName, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    // --- Owner scope ------------------------------------------------------------------------------

    [Fact]
    public async Task Uploading_a_cv_without_a_token_is_a_401()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            new Uri("/api/cv", UriKind.Relative),
            File(Encoding.UTF8.GetBytes("# CV"), "cv.md", "text/markdown"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Uploading_a_cv_as_a_different_subject_is_a_403()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, "jobhunter:read");
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, "someone-else");

        var response = await client.PostAsync(
            new Uri("/api/cv", UriKind.Relative),
            File(Encoding.UTF8.GetBytes("# CV"), "cv.md", "text/markdown"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Happy path -------------------------------------------------------------------------------

    [Fact]
    public async Task Uploading_a_new_cv_returns_201_with_the_version_and_no_cv_text()
    {
        var profile = ActiveProfile();
        HasActiveProfile(profile);
        _factory.CvVersions.NextVersionAsync(profile.Id, Arg.Any<CancellationToken>()).Returns((short)1);
        const string secret = "SENTINEL-CV-CONTENT";
        _factory.CvTextExtractor.Extract(CvMediaType.Markdown, Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(Result<string>.Success(secret));

        using var client = _factory.OwnerClient();
        var response = await client.PostAsync(
            new Uri("/api/cv", UriKind.Relative),
            File(Encoding.UTF8.GetBytes("# CV\n" + secret), "cv.md", "text/markdown"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain("SENTINEL");
        var body = await response.Content.ReadFromJsonAsync<CvVersionResponse>();
        body.ShouldNotBeNull();
        body.Version.ShouldBe((short)1);
        body.Created.ShouldBeTrue();
    }

    // --- Sniffed, not trusted ---------------------------------------------------------------------

    [Fact]
    public async Task A_zip_declared_as_pdf_is_a_400()
    {
        HasActiveProfile(ActiveProfile());
        byte[] zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];

        using var client = _factory.OwnerClient();
        var response = await client.PostAsync(
            new Uri("/api/cv", UriKind.Relative),
            File(zip, "cv.pdf", "application/pdf"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _factory.CvTextExtractor.DidNotReceiveWithAnyArgs().Extract(default, default);
    }

    // --- Oversize before extraction ---------------------------------------------------------------

    [Fact]
    public async Task A_file_over_the_cap_is_a_413_and_is_never_extracted()
    {
        HasActiveProfile(ActiveProfile());
        var oversize = new byte[Application.Profiles.CvUploadService.MaxSizeBytes + 1];
        oversize[0] = (byte)'%';

        using var client = _factory.OwnerClient();
        var response = await client.PostAsync(
            new Uri("/api/cv", UriKind.Relative),
            File(oversize, "cv.pdf", "application/pdf"));

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        _factory.CvTextExtractor.DidNotReceiveWithAnyArgs().Extract(default, default);
    }

    // --- No OCR -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_pdf_with_no_extractable_text_is_a_400()
    {
        HasActiveProfile(ActiveProfile());
        _factory.CvTextExtractor.Extract(CvMediaType.Pdf, Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(Result<string>.Failure(new Error(
                Application.Profiles.CvUploadService.Errors.NoText, "A scan is not supported.")));

        using var client = _factory.OwnerClient();
        var response = await client.PostAsync(
            new Uri("/api/cv", UriKind.Relative),
            File(Encoding.ASCII.GetBytes("%PDF-1.7\nscan"), "cv.pdf", "application/pdf"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Content-hash no-op -----------------------------------------------------------------------

    [Fact]
    public async Task Re_uploading_identical_content_is_a_200_no_op()
    {
        var profile = ActiveProfile();
        HasActiveProfile(profile);
        var bytes = Encoding.UTF8.GetBytes("# CV\nSame bytes.");
        var existing = new CvVersion(
            Guid.CreateVersion7(), profile.Id, version: 2, isActive: true, "cv.md", "text/markdown",
            bytes.Length, new string('a', 64), "Same bytes.", Now, Now);
        _factory.CvVersions.FindByHashAsync(profile.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        using var client = _factory.OwnerClient();
        var response = await client.PostAsync(
            new Uri("/api/cv", UriKind.Relative),
            File(bytes, "cv.md", "text/markdown"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CvVersionResponse>();
        body.ShouldNotBeNull();
        body.Created.ShouldBeFalse();
        body.Version.ShouldBe((short)2);
    }

    // --- Empty ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_empty_upload_is_a_400()
    {
        HasActiveProfile(ActiveProfile());

        using var client = _factory.OwnerClient();
        var response = await client.PostAsync(
            new Uri("/api/cv", UriKind.Relative),
            File([], "cv.md", "text/markdown"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
