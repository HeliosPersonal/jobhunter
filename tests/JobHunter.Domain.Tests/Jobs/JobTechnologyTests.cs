using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Jobs;

public sealed class JobTechnologyTests
{
    private static readonly Guid JobId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void A_valid_technology_tag_carries_its_match_source()
    {
        var tag = new JobTechnology(JobId, "C#", TechnologyMatch.Title);

        tag.JobId.ShouldBe(JobId);
        tag.Technology.ShouldBe("C#");
        tag.MatchedVia.ShouldBe(TechnologyMatch.Title);
    }

    [Fact]
    public void An_empty_job_id_is_rejected()
    {
        Should.Throw<ArgumentException>(() => new JobTechnology(Guid.Empty, "C#", TechnologyMatch.Title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_technology_is_rejected(string technology)
    {
        Should.Throw<ArgumentException>(() => new JobTechnology(JobId, technology, TechnologyMatch.Title));
    }
}
