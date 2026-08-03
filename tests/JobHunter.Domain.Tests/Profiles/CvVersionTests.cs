using JobHunter.Domain.Profiles;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Profiles;

public sealed class CvVersionTests
{
    private static readonly Guid CvId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");
    private static readonly Guid ProfileId = Guid.Parse("00000000-0000-0000-0000-0000000000F1");
    private const string ValidHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static CvVersion NewCv(
        short version = 1,
        bool isActive = true,
        string fileName = "cv.pdf",
        string mediaType = "application/pdf",
        int sizeBytes = 2048,
        string contentHash = ValidHash,
        string extractedText = "Senior platform engineer, Go and Kubernetes.",
        DateTimeOffset? activatedAt = null)
    {
        var clock = new FakeClock();
        return new CvVersion(
            CvId,
            ProfileId,
            version,
            isActive,
            fileName,
            mediaType,
            sizeBytes,
            contentHash,
            extractedText,
            clock.UtcNow,
            activatedAt);
    }

    [Fact]
    public void A_valid_cv_version_exposes_its_fields()
    {
        var activatedAt = new FakeClock().UtcNow;
        var cv = NewCv(activatedAt: activatedAt);

        cv.Id.ShouldBe(CvId);
        cv.ProfileId.ShouldBe(ProfileId);
        cv.Version.ShouldBe((short)1);
        cv.IsActive.ShouldBeTrue();
        cv.FileName.ShouldBe("cv.pdf");
        cv.MediaType.ShouldBe("application/pdf");
        cv.SizeBytes.ShouldBe(2048);
        cv.ContentHash.ShouldBe(ValidHash);
        cv.ExtractedText.ShouldBe("Senior platform engineer, Go and Kubernetes.");
        cv.ActivatedAt.ShouldBe(activatedAt);
    }

    [Fact]
    public void An_empty_profile_id_is_rejected()
    {
        var clock = new FakeClock();
        Should.Throw<ArgumentException>(() => new CvVersion(
            CvId, Guid.Empty, 1, true, "cv.pdf", "application/pdf", 2048, ValidHash, "text", clock.UtcNow, null));
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)-1)]
    public void A_non_positive_version_is_rejected(short version)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewCv(version: version));
    }

    [Fact]
    public void A_non_positive_size_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewCv(sizeBytes: 0));
    }

    [Fact]
    public void A_blank_extracted_text_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewCv(extractedText: "   "));
    }

    [Fact]
    public void A_blank_file_name_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewCv(fileName: " "));
    }

    [Fact]
    public void A_blank_media_type_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewCv(mediaType: " "));
    }

    [Theory]
    [InlineData("tooshort")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")] // 'g' is not hex
    public void A_malformed_content_hash_is_rejected(string hash)
    {
        Should.Throw<ArgumentException>(() => NewCv(contentHash: hash));
    }

    [Fact]
    public void An_upper_case_hash_is_normalised_to_lower_case()
    {
        var cv = NewCv(contentHash: ValidHash.ToUpperInvariant());

        cv.ContentHash.ShouldBe(ValidHash);
    }

    [Fact]
    public void Extracted_text_has_no_public_setter()
    {
        // The single CV-bearing property is immutable: there is no way to reassign it after construction.
        var property = typeof(CvVersion).GetProperty(nameof(CvVersion.ExtractedText));
        property.ShouldNotBeNull();
        property!.SetMethod?.IsPublic.ShouldNotBe(true);
    }
}
