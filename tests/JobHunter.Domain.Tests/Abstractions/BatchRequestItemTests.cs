using JobHunter.Domain.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Abstractions;

/// <summary>
/// The port-contract shape the CV prompt cache rides on (F4 T13, ADR-F4-0003). The cache split is a wire
/// concern owned by the adapter, so the domain record only has to guarantee two things: an item without a
/// prefix behaves exactly as before (the whole message is the user content), and an item with one exposes
/// the pessimistic whole — prefix then content — that the cost accountant prices and the fallback sends.
/// </summary>
public sealed class BatchRequestItemTests
{
    private static readonly JsonSchema Schema = new("match", "{\"type\":\"object\"}");

    [Fact]
    public void Without_a_cache_prefix_the_full_user_content_is_the_user_content_verbatim()
    {
        var item = new BatchRequestItem("job-1", "sys", "the whole prompt", Schema);

        item.CachePrefix.ShouldBeNull();
        item.FullUserContent.ShouldBe("the whole prompt");
    }

    [Fact]
    public void With_a_cache_prefix_the_full_user_content_is_the_prefix_then_the_content()
    {
        var item = new BatchRequestItem("job-1", "sys", "ROLE", Schema, CachePrefix: "CV PREFIX");

        // The pessimistic whole the accountant prices: the cache discount is never assumed up front.
        item.FullUserContent.ShouldBe("CV PREFIX\nROLE");
    }

    [Fact]
    public void Token_usage_defaults_cache_read_to_zero()
    {
        new TokenUsage(4000, 300).CacheReadInputTokens.ShouldBe(0);
        TokenUsage.Zero.CacheReadInputTokens.ShouldBe(0);
        new TokenUsage(300, 300, 2400).CacheReadInputTokens.ShouldBe(2400);
    }
}
