using JobHunter.Domain.Companies;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T04: the F2 read port over stored raw postings. It returns the verbatim payload and the timestamps that
/// become a job's first/last seen, keyed by id, and null when the id is unknown — the read-only path F2
/// normalisation and reprocessing take over the F1-owned, immutable <c>raw_postings</c> table. Requires Docker.
/// </summary>
public sealed class RawPostingReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 7, 0, 0, TimeSpan.Zero);

    private static async Task<(TestDatabase Db, Guid SourceId)> SeededSourceAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        await ctx.SaveChangesAsync();

        return (database, sourceId);
    }

    [RequiresDockerFact]
    public async Task It_returns_the_stored_payload_and_timestamps_for_a_known_id()
    {
        var (database, sourceId) = await SeededSourceAsync();
        await using var _ = database;
        var factory = new NpgsqlConnectionFactory(database.ConnectionString);

        const string payload = "{\"title\":\"SRE\"}";
        var posting = new RawPosting(Guid.CreateVersion7(), sourceId, "job-1", ContentHash.Compute(payload), payload, 200, Now);
        await new RawPostingRepository(factory).IngestAsync(posting);

        var content = await new RawPostingReaderQuery(factory).FindAsync(posting.Id);

        content.ShouldNotBeNull();
        content!.Id.ShouldBe(posting.Id);
        content.SourceId.ShouldBe(sourceId);
        content.ExternalId.ShouldBe("job-1");
        content.FetchedAt.ToUniversalTime().ShouldBe(Now);
        content.LastSeenAt.ToUniversalTime().ShouldBe(Now);
        System.Text.Json.JsonDocument.Parse(content.Payload)
            .RootElement.GetProperty("title").GetString().ShouldBe("SRE");
    }

    [RequiresDockerFact]
    public async Task It_returns_null_for_an_unknown_id()
    {
        var (database, _) = await SeededSourceAsync();
        await using var db = database;
        var reader = new RawPostingReaderQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        (await reader.FindAsync(Guid.CreateVersion7())).ShouldBeNull();
    }
}
