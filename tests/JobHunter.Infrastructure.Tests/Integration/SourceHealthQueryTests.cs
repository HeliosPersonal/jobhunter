using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F10 T09 / R4: the source-health query rolls up <c>source_fetch_log</c> by ATS provider over a trailing
/// window — attempts, successes and the last attempt — so <c>/sources</c> can show which integration is
/// failing. It groups every source a provider backs into one line, counts only attempts inside the window,
/// and treats the <c>Success</c> outcome as a success and every other outcome as a failed attempt. Requires
/// Docker.
/// </summary>
public sealed class SourceHealthQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Rolls_up_attempts_and_successes_by_provider_within_the_window()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var ctx = database.CreateContext())
        {
            // Greenhouse, one source: two attempts in-window (one success, one HTTP error), one stale attempt
            // outside the window that must not be counted.
            var greenhouse = Seed(ctx, AtsKind.Greenhouse, "acme.com", "Acme", "acme");
            Log(ctx, greenhouse, Now.AddHours(-2), FetchOutcome.Success, 200, 12);
            Log(ctx, greenhouse, Now.AddHours(-1), FetchOutcome.HttpError, 503, 0);
            Log(ctx, greenhouse, Now.AddHours(-30), FetchOutcome.Success, 200, 5); // stale — excluded

            // Lever, one source: a single successful attempt in-window.
            var lever = Seed(ctx, AtsKind.Lever, "globex.com", "Globex", "globex");
            Log(ctx, lever, Now.AddHours(-3), FetchOutcome.Success, 200, 4);

            await ctx.SaveChangesAsync();
        }

        var query = new SourceHealthQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        var health = await query.HealthSinceAsync(Now.AddHours(-24));

        health.Count.ShouldBe(2);

        var gh = health.Single(h => h.AtsKind == nameof(AtsKind.Greenhouse));
        gh.Attempts.ShouldBe(2);      // the stale attempt is outside the window
        gh.Successes.ShouldBe(1);
        gh.LastAttemptAt.ToUniversalTime().ShouldBe(Now.AddHours(-1));

        var lv = health.Single(h => h.AtsKind == nameof(AtsKind.Lever));
        lv.Attempts.ShouldBe(1);
        lv.Successes.ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task Returns_nothing_when_no_attempt_falls_inside_the_window()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var ctx = database.CreateContext())
        {
            var greenhouse = Seed(ctx, AtsKind.Greenhouse, "acme.com", "Acme", "acme");
            Log(ctx, greenhouse, Now.AddHours(-40), FetchOutcome.Success, 200, 12);
            await ctx.SaveChangesAsync();
        }

        var query = new SourceHealthQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        var health = await query.HealthSinceAsync(Now.AddHours(-24));

        health.ShouldBeEmpty();
    }

    private static JobSource Seed(JobHunterDbContext ctx, AtsKind kind, string domain, string name, string token)
    {
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();

        var company = new Company(companyId, CanonicalDomain.TryCreate(domain).Value, name, CompanySource.Curated, Now);
        var binding = new AtsBinding(bindingId, companyId, kind, token, BindingConfidence.TryCreate(0.9m).Value, "{}", Now);
        var source = new JobSource(sourceId, companyId, bindingId, $"https://example.com/{token}/jobs");

        ctx.Add(company);
        ctx.Add(binding);
        ctx.Add(source);
        return source;
    }

    private static void Log(
        JobHunterDbContext ctx, JobSource source, DateTimeOffset at, FetchOutcome outcome, short status, int postings)
    {
        ctx.Add(new SourceFetchLog(
            Guid.CreateVersion7(), source.Id, at, durationMs: 100, httpStatus: status,
            postingsReturned: postings, postingsChanged: postings, outcome: outcome));
    }
}
