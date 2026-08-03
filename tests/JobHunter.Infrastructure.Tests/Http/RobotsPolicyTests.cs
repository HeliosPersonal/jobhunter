using JobHunter.Infrastructure.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

public sealed class RobotsPolicyTests
{
    private const string UserAgent = "JobHunter/1.0 (+https://github.com/jobhunter/jobhunter; contact@x)";

    private static (RobotsPolicy Policy, Counter Fetches) Build(RobotsPolicy.FetchRobots fetch)
    {
        var counter = new Counter();
        RobotsPolicy.FetchRobots counted = (url, ct) =>
        {
            counter.Count++;
            return fetch(url, ct);
        };
        var policy = new RobotsPolicy(
            counted,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new PolitenessOptions()));
        return (policy, counter);
    }

    [Fact]
    public async Task A_disallowed_path_is_refused()
    {
        var (policy, _) = Build((_, _) => Task.FromResult(
            RobotsPolicy.RobotsFetch.Ok("User-agent: *\nDisallow: /private")));

        var allowed = await policy.IsAllowedAsync(new Uri("https://boards.example/private/jobs"), UserAgent);

        allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task An_allowed_path_is_permitted()
    {
        var (policy, _) = Build((_, _) => Task.FromResult(
            RobotsPolicy.RobotsFetch.Ok("User-agent: *\nDisallow: /private")));

        var allowed = await policy.IsAllowedAsync(new Uri("https://boards.example/jobs"), UserAgent);

        allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unreachable_robots_reads_permissively()
    {
        var (policy, _) = Build((_, _) => Task.FromResult(RobotsPolicy.RobotsFetch.NotReachable));

        var allowed = await policy.IsAllowedAsync(new Uri("https://boards.example/anything"), UserAgent);

        allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_malformed_robots_reads_conservatively()
    {
        var (policy, _) = Build((_, _) => Task.FromResult(RobotsPolicy.RobotsFetch.WasMalformed));

        var allowed = await policy.IsAllowedAsync(new Uri("https://boards.example/jobs"), UserAgent);

        allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task The_result_is_cached_per_host_and_fetched_once()
    {
        var (policy, fetches) = Build((_, _) => Task.FromResult(
            RobotsPolicy.RobotsFetch.Ok("User-agent: *\nDisallow: /private")));

        await policy.IsAllowedAsync(new Uri("https://boards.example/a"), UserAgent);
        await policy.IsAllowedAsync(new Uri("https://boards.example/b"), UserAgent);
        await policy.IsAllowedAsync(new Uri("https://boards.example/private/x"), UserAgent);

        fetches.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Different_hosts_are_fetched_independently()
    {
        var (policy, fetches) = Build((_, _) => Task.FromResult(
            RobotsPolicy.RobotsFetch.Ok(string.Empty)));

        await policy.IsAllowedAsync(new Uri("https://a.example/x"), UserAgent);
        await policy.IsAllowedAsync(new Uri("https://b.example/x"), UserAgent);

        fetches.Count.ShouldBe(2);
    }

    [Fact]
    public async Task robots_is_fetched_from_the_origin_root()
    {
        Uri? requested = null;
        var (policy, _) = Build((url, _) =>
        {
            requested = url;
            return Task.FromResult(RobotsPolicy.RobotsFetch.Ok(string.Empty));
        });

        await policy.IsAllowedAsync(new Uri("https://boards.example/deep/path/jobs"), UserAgent);

        requested.ShouldBe(new Uri("https://boards.example/robots.txt"));
    }

    [Fact]
    public async Task A_blank_user_agent_is_rejected()
    {
        var (policy, _) = Build((_, _) => Task.FromResult(RobotsPolicy.RobotsFetch.NotReachable));

        await Should.ThrowAsync<ArgumentException>(
            () => policy.IsAllowedAsync(new Uri("https://boards.example/x"), " "));
    }

    private sealed class Counter
    {
        public int Count { get; set; }
    }
}
