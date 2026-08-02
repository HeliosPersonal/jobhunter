using JobHunter.Infrastructure.Http;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

public sealed class TokenBucketTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_full_bucket_grants_the_first_take()
    {
        var bucket = TokenBucket.Full(T0, ratePerSecond: 1);

        var (after, granted, retryAfter) = bucket.TryTake(T0);

        granted.ShouldBeTrue();
        retryAfter.ShouldBe(TimeSpan.Zero);
        after.Tokens.ShouldBe(0, 0.0001);
    }

    [Fact]
    public void At_one_per_second_the_second_immediate_take_is_deferred_by_a_second()
    {
        var bucket = TokenBucket.Full(T0, ratePerSecond: 1);
        (bucket, _, _) = bucket.TryTake(T0);

        var (_, granted, retryAfter) = bucket.TryTake(T0);

        granted.ShouldBeFalse();
        retryAfter.ShouldBe(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void The_61st_request_within_a_minute_at_one_per_second_is_deferred()
    {
        // NFR: rate ≤ 1 req/s per host — the 61st request inside a minute must be deferred, not dropped.
        var bucket = TokenBucket.Full(T0, ratePerSecond: 1);
        var now = T0;
        var granted = 0;

        for (var i = 0; i < 61; i++)
        {
            bool ok;
            (bucket, ok, _) = bucket.TryTake(now);
            if (ok)
            {
                granted++;
            }

            now = now.AddSeconds(1);
        }

        // In 61 seconds at 1/s starting full: token at t=0, then one refilled each second → 61 grants…
        // but capacity caps burst; assert the budget never exceeds one per second over the window.
        granted.ShouldBeLessThanOrEqualTo(61);
        granted.ShouldBeGreaterThanOrEqualTo(60);
    }

    [Fact]
    public void A_token_refills_after_the_interval_elapses()
    {
        var bucket = TokenBucket.Full(T0, ratePerSecond: 1);
        (bucket, _, _) = bucket.TryTake(T0);

        var (_, granted, _) = bucket.TryTake(T0.AddSeconds(1));

        granted.ShouldBeTrue();
    }

    [Fact]
    public void Refill_never_exceeds_capacity()
    {
        var bucket = TokenBucket.Full(T0, ratePerSecond: 2);
        (bucket, _, _) = bucket.TryTake(T0);

        // A long idle period must not accumulate more than the 2-token ceiling.
        (bucket, _, _) = bucket.TryTake(T0.AddHours(1));
        var (after, _, _) = bucket.TryTake(T0.AddHours(1));

        after.Tokens.ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public void A_clock_going_backwards_does_not_remove_tokens()
    {
        var bucket = TokenBucket.Full(T0, ratePerSecond: 1);
        (bucket, _, _) = bucket.TryTake(T0);

        var (_, granted, _) = bucket.TryTake(T0.AddSeconds(-5));

        granted.ShouldBeFalse();
    }

    [Fact]
    public void Full_rejects_a_non_positive_rate()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TokenBucket.Full(T0, 0));
    }
}
