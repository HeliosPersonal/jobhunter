using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace JobHunter.Infrastructure.Http;

/// <summary>
/// The per-host token bucket, held in Redis so the budget survives a pod restart (SAD §7, §8). One
/// atomic Lua script refills the bucket by elapsed time and takes a token in a single round trip, so
/// concurrent fetchers on different pods share one budget with no read-then-write race. A denied take
/// returns the exact wait until the next token, which the handler defers on rather than dropping.
/// <c>Retry-After</c> penalties are stored as an absolute "blocked until" instant that the take script
/// respects and never shortens (AC-07).
/// </summary>
internal sealed class RedisTokenBucket(
    IConnectionMultiplexer multiplexer,
    IClock clock,
    IOptions<PolitenessOptions> options) : IRateLimiter
{
    // KEYS[1] tokens, KEYS[2] timestamp(ms), KEYS[3] blockedUntil(ms).
    // ARGV[1] ratePerSecond, ARGV[2] nowMs. Returns {granted(0/1), retryAfterMs}.
    private const string TakeScript =
        """
        local rate = tonumber(ARGV[1])
        local now = tonumber(ARGV[2])
        local blockedUntil = tonumber(redis.call('GET', KEYS[3]) or '0')
        if now < blockedUntil then
            return {0, blockedUntil - now}
        end
        local tokens = tonumber(redis.call('GET', KEYS[1]) or tostring(rate))
        local last = tonumber(redis.call('GET', KEYS[2]) or tostring(now))
        local refill = (now - last) / 1000.0 * rate
        tokens = math.min(rate, tokens + refill)
        local ttl = math.ceil(rate / rate) + 3600
        if tokens >= 1 then
            tokens = tokens - 1
            redis.call('SET', KEYS[1], tostring(tokens), 'EX', ttl)
            redis.call('SET', KEYS[2], tostring(now), 'EX', ttl)
            return {1, 0}
        end
        local needed = (1 - tokens) / rate * 1000.0
        redis.call('SET', KEYS[1], tostring(tokens), 'EX', ttl)
        redis.call('SET', KEYS[2], tostring(now), 'EX', ttl)
        return {0, math.ceil(needed)}
        """;

    private readonly PolitenessOptions _options = options.Value;

    public async Task<RateLease> AcquireAsync(string host, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        cancellationToken.ThrowIfCancellationRequested();

        var db = multiplexer.GetDatabase();
        var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
        var keys = Keys(host);

        var result = (RedisResult[])(await db.ScriptEvaluateAsync(
            TakeScript,
            keys,
            [_options.DefaultRequestsPerSecond, nowMs]).ConfigureAwait(false))!;

        var granted = (long)result[0] == 1;
        var retryAfterMs = (long)result[1];
        return granted ? RateLease.Allow : RateLease.Deny(TimeSpan.FromMilliseconds(retryAfterMs));
    }

    public async Task PenaliseAsync(string host, TimeSpan retryAfter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (retryAfter <= TimeSpan.Zero)
        {
            return;
        }

        var db = multiplexer.GetDatabase();
        var blockedUntil = clock.UtcNow.Add(retryAfter).ToUnixTimeMilliseconds();
        var key = BlockedKey(host);

        // Never shorten an existing, longer block: keep the maximum of the two instants (AC-07).
        var existing = (long?)await db.StringGetAsync(key).ConfigureAwait(false) ?? 0;
        var target = Math.Max(existing, blockedUntil);
        var ttl = retryAfter + TimeSpan.FromMinutes(1);
        await db.StringSetAsync(key, target, ttl).ConfigureAwait(false);
    }

    private RedisKey[] Keys(string host) =>
        [
            $"{_options.RateLimitKeyPrefix}:{host}:tokens",
            $"{_options.RateLimitKeyPrefix}:{host}:ts",
            BlockedKey(host),
        ];

    private RedisKey BlockedKey(string host) => $"{_options.RateLimitKeyPrefix}:{host}:blocked";
}
