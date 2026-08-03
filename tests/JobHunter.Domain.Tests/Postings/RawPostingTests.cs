using JobHunter.Domain.Postings;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Postings;

public sealed class RawPostingTests
{
    private static readonly Guid PostingId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid SourceId = Guid.Parse("00000000-0000-0000-0000-0000000000A2");
    private static readonly DateTimeOffset FetchedAt = new(2026, 8, 2, 7, 0, 0, TimeSpan.Zero);

    private static ContentHash Hash() => ContentHash.Compute("{\"title\":\"SRE\"}");

    [Fact]
    public void New_posting_seeds_last_seen_from_fetched_at()
    {
        var posting = new RawPosting(PostingId, SourceId, "job-1", Hash(), "{\"title\":\"SRE\"}", 200, FetchedAt);

        posting.SourceId.ShouldBe(SourceId);
        posting.ExternalId.ShouldBe("job-1");
        posting.HttpStatus.ShouldBe((short)200);
        posting.FetchedAt.ShouldBe(FetchedAt);
        posting.LastSeenAt.ShouldBe(FetchedAt);
    }

    [Fact]
    public void Constructor_rejects_an_empty_source_id()
    {
        Should.Throw<ArgumentException>(() =>
            new RawPosting(PostingId, Guid.Empty, "job-1", Hash(), "{}", 200, FetchedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_a_blank_external_id(string externalId)
    {
        Should.Throw<ArgumentException>(() =>
            new RawPosting(PostingId, SourceId, externalId, Hash(), "{}", 200, FetchedAt));
    }
}
