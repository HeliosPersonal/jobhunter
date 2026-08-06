using JobHunter.Application.Applications;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T05, AC-07 (the Messaging suite, Testcontainers): the closure handler consuming <see cref="JobClosed"/>,
/// proven against a real database. A closed posting marks the application and records a
/// <see cref="TransitionSource.System"/> self-transition — <b>without changing the status</b> — and the full
/// history is retained; nothing is deleted. A closure for a terminal application is a no-op. Requires Docker.
/// </summary>
public sealed class JobClosureHandlerIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 6, 0, 0, TimeSpan.Zero);
    private static readonly ReminderPolicy Policy = ReminderPolicy.Default;

    [RequiresDockerFact]
    public async Task JobClosure_MarksApplication_WithoutChangingStatus_AndRetainsHistory()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        // An application in Interview — two real moves plus the creating row — is the record to preserve.
        var app = App.Create(Guid.CreateVersion7(), seed.JobId, Now.AddDays(-5), TransitionSource.Telegram);
        app.ChangeStatus(ApplicationStatus.Applied, TransitionSource.Telegram, Now.AddDays(-3), Policy);
        app.ChangeStatus(ApplicationStatus.Interview, TransitionSource.Telegram, Now.AddDays(-1), Policy);
        var write = new ApplicationRepository(seed.Database.CreateContext());
        write.Add(app);
        await write.SaveChangesAsync();

        var bus = Substitute.For<IMessageBus>();
        var handler = new JobClosureHandler(
            new ApplicationRepository(seed.Database.CreateContext()),
            NullLogger<JobClosureHandler>.Instance);

        await handler.Handle(
            new JobClosed(seed.JobId, Now, "StaleAcrossAllSources", Now), bus, CancellationToken.None);

        var loaded = await new ApplicationRepository(seed.Database.CreateContext()).FindByJobAsync(seed.JobId);
        loaded.ShouldNotBeNull();
        loaded.PostingClosed.ShouldBeTrue();
        // AC-07: the status is untouched — a closed posting is not a rejection.
        loaded.Status.ShouldBe(ApplicationStatus.Interview);
        // The full history is retained, with the closure appended as a System self-transition.
        loaded.Transitions.Select(t => t.To).ShouldBe(
        [
            ApplicationStatus.New,
            ApplicationStatus.Applied,
            ApplicationStatus.Interview,
            ApplicationStatus.Interview,
        ]);
        var closure = loaded.Transitions[^1];
        closure.From.ShouldBe(ApplicationStatus.Interview);
        closure.Source.ShouldBe(TransitionSource.System);
        closure.Detail.ShouldBe(App.PostingClosedDetail);
        // The status did not change, so nothing is published.
        await bus.DidNotReceive().PublishAsync(Arg.Any<ApplicationStatusChanged>());
    }

    [RequiresDockerFact]
    public async Task A_closure_for_a_terminal_application_marks_nothing()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var app = App.Create(Guid.CreateVersion7(), seed.JobId, Now, TransitionSource.Telegram);
        app.ChangeStatus(ApplicationStatus.Rejected, TransitionSource.Telegram, Now.AddDays(-1), Policy);
        var write = new ApplicationRepository(seed.Database.CreateContext());
        write.Add(app);
        await write.SaveChangesAsync();

        var handler = new JobClosureHandler(
            new ApplicationRepository(seed.Database.CreateContext()),
            NullLogger<JobClosureHandler>.Instance);

        await handler.Handle(
            new JobClosed(seed.JobId, Now, "StaleAcrossAllSources", Now),
            Substitute.For<IMessageBus>(), CancellationToken.None);

        var loaded = await new ApplicationRepository(seed.Database.CreateContext()).FindByJobAsync(seed.JobId);
        loaded.ShouldNotBeNull();
        // The outcome is already known — nothing marked, no closure transition recorded (SAD §6.3).
        loaded.PostingClosed.ShouldBeFalse();
        loaded.Transitions.Any(t => t.Detail == App.PostingClosedDetail).ShouldBeFalse();
    }

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, "job-1", ContentHash.Compute("{\"t\":\"x\"}"), "{\"t\":\"x\"}", 200, Now));
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(new string('a', 64)).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "d",
            applyUrl: "https://acme.com/apply/1", LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid, EmploymentType.FullTime, PostedAtGranularity.Day, firstSeenAt: Now, lastSeenAt: Now));
        await ctx.SaveChangesAsync();

        return new Seed(database, jobId);
    }

    private sealed record Seed(TestDatabase Database, Guid JobId);
}
