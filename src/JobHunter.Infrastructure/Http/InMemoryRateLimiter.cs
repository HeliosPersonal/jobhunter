using System.Collections.Concurrent;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Http;

/// <summary>
/// A single-process <see cref="IRateLimiter"/> built on the pure <see cref="TokenBucket"/> arithmetic.
/// It is the default budget when no Redis is configured (Redis is optional — the system degrades to
/// DB/in-process paths, per <c>ConnectionStringOptions.Cache</c>) and the fixture-friendly limiter the
/// unit tests drive with a <see cref="IClock"/> that never touches real time. Buckets and penalties are
/// held per host; a penalty is an absolute "blocked until" instant that is never shortened (AC-07).
/// </summary>
internal sealed class InMemoryRateLimiter(IClock clock, IOptions<PolitenessOptions> options) : IRateLimiter
{
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _blockedUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _rate = Math.Max(1, options.Value.DefaultRequestsPerSecond);
    private readonly object _gate = new();

    public Task<RateLease> AcquireAsync(string host, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        cancellationToken.ThrowIfCancellationRequested();

        var now = clock.UtcNow;

        lock (_gate)
        {
            if (_blockedUntil.TryGetValue(host, out var until) && now < until)
            {
                return Task.FromResult(RateLease.Deny(until - now));
            }

            var bucket = _buckets.TryGetValue(host, out var existing)
                ? existing
                : TokenBucket.Full(now, _rate);

            var (updated, granted, retryAfter) = bucket.TryTake(now);
            _buckets[host] = updated;

            return Task.FromResult(granted ? RateLease.Allow : RateLease.Deny(retryAfter));
        }
    }

    public Task PenaliseAsync(string host, TimeSpan retryAfter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (retryAfter <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        var target = clock.UtcNow.Add(retryAfter);

        lock (_gate)
        {
            // Never shorten a longer, existing block (AC-07).
            _blockedUntil.AddOrUpdate(host, target, (_, current) => target > current ? target : current);
        }

        return Task.CompletedTask;
    }
}
