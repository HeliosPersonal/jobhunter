using JobHunter.Domain.Research;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Research;

/// <summary>
/// The eight research categories are a closed set (SAD §8, T01 done-when 2): a fetcher exists per
/// category, and a claim can only ever belong to one of them. Locking the count here is the guard that a
/// ninth is a deliberate schema change, not an accident.
/// </summary>
public sealed class ResearchCategoryTests
{
    [Fact]
    public void There_are_exactly_eight_categories()
    {
        Enum.GetValues<ResearchCategory>().Length.ShouldBe(8);
    }

    [Theory]
    [InlineData(ResearchCategory.Funding)]
    [InlineData(ResearchCategory.EngineeringBlog)]
    [InlineData(ResearchCategory.OpenSource)]
    [InlineData(ResearchCategory.Reviews)]
    [InlineData(ResearchCategory.News)]
    [InlineData(ResearchCategory.Layoffs)]
    [InlineData(ResearchCategory.Stack)]
    [InlineData(ResearchCategory.InterviewProcess)]
    public void The_eight_named_categories_are_all_present(ResearchCategory category)
    {
        Enum.IsDefined(category).ShouldBeTrue();
    }
}
