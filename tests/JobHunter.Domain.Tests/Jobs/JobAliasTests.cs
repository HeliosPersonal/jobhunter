using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Jobs;

public sealed class JobAliasTests
{
    private static readonly Guid JobId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PostingId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Seen = new(2026, 1, 1, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_valid_alias_carries_its_provenance()
    {
        var alias = new JobAlias(JobId, PostingId, SourceId, Seen, Seen);

        alias.JobId.ShouldBe(JobId);
        alias.RawPostingId.ShouldBe(PostingId);
        alias.SourceId.ShouldBe(SourceId);
        alias.FirstSeenAt.ShouldBe(Seen);
        alias.LastSeenAt.ShouldBe(Seen);
    }

    [Fact]
    public void An_empty_job_id_is_rejected()
    {
        Should.Throw<ArgumentException>(() => new JobAlias(Guid.Empty, PostingId, SourceId, Seen, Seen));
    }

    [Fact]
    public void An_empty_posting_id_is_rejected()
    {
        Should.Throw<ArgumentException>(() => new JobAlias(JobId, Guid.Empty, SourceId, Seen, Seen));
    }

    [Fact]
    public void An_empty_source_id_is_rejected()
    {
        Should.Throw<ArgumentException>(() => new JobAlias(JobId, PostingId, Guid.Empty, Seen, Seen));
    }
}
