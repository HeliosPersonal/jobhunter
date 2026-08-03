using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// AC-02 / QG-3: the raw-posting upsert deduplicates on <c>(source_id, external_id, content_hash)</c>,
/// reports a genuine insert distinctly from a conflict via the <c>xmax = 0</c> trick, bumps
/// <c>last_seen_at</c> on an unchanged re-fetch, and never edits the stored payload. Requires Docker.
/// </summary>
public sealed class RawPostingIngestionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 7, 0, 0, TimeSpan.Zero);

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

    private static RawPosting Posting(Guid sourceId, string externalId, string payload, DateTimeOffset at) =>
        new(Guid.CreateVersion7(), sourceId, externalId, ContentHash.Compute(payload), payload, 200, at);

    [RequiresDockerFact]
    public async Task First_ingest_reports_inserted_second_identical_reports_unchanged_and_bumps_last_seen()
    {
        var (database, sourceId) = await SeededSourceAsync();
        await using var _ = database;
        var factory = new NpgsqlConnectionFactory(database.ConnectionString);
        var repo = new RawPostingRepository(factory);

        var first = Posting(sourceId, "job-1", "{\"title\":\"SRE\"}", Now);
        (await repo.IngestAsync(first)).ShouldBe(IngestOutcome.Inserted);

        var later = Now.AddHours(6);
        var second = Posting(sourceId, "job-1", "{\"title\":\"SRE\"}", later);
        (await repo.IngestAsync(second)).ShouldBe(IngestOutcome.Unchanged);

        await using var connection = new Npgsql.NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MAX(last_seen_at) FROM raw_postings WHERE source_id = @s";
        command.Parameters.AddWithValue("s", sourceId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt64(0).ShouldBe(1);
        reader.GetFieldValue<DateTimeOffset>(1).ToUniversalTime().ShouldBe(later);
    }

    [RequiresDockerFact]
    public async Task Changed_content_for_the_same_external_id_inserts_a_new_row()
    {
        var (database, sourceId) = await SeededSourceAsync();
        await using var _ = database;
        var repo = new RawPostingRepository(new NpgsqlConnectionFactory(database.ConnectionString));

        (await repo.IngestAsync(Posting(sourceId, "job-1", "{\"title\":\"SRE\"}", Now))).ShouldBe(IngestOutcome.Inserted);
        (await repo.IngestAsync(Posting(sourceId, "job-1", "{\"title\":\"Staff SRE\"}", Now))).ShouldBe(IngestOutcome.Inserted);

        await using var read = database.CreateContext();
        var count = await read.Set<RawPosting>().CountAsync(x => x.SourceId == sourceId);
        count.ShouldBe(2);
    }

    [RequiresDockerFact]
    public async Task The_stored_payload_is_never_edited_on_re_ingest()
    {
        var (database, sourceId) = await SeededSourceAsync();
        await using var _ = database;
        var repo = new RawPostingRepository(new NpgsqlConnectionFactory(database.ConnectionString));

        const string payload = "{\"title\":\"SRE\",\"team\":\"infra\"}";
        await repo.IngestAsync(Posting(sourceId, "job-1", payload, Now));
        await repo.IngestAsync(Posting(sourceId, "job-1", payload, Now.AddDays(1)));

        await using var read = database.CreateContext();
        var stored = await read.Set<RawPosting>().SingleAsync(x => x.SourceId == sourceId);

        // Stored as jsonb, so whitespace/key-order is canonicalised; the content is unchanged.
        using var storedJson = System.Text.Json.JsonDocument.Parse(stored.Payload);
        storedJson.RootElement.GetProperty("title").GetString().ShouldBe("SRE");
        storedJson.RootElement.GetProperty("team").GetString().ShouldBe("infra");
        stored.FetchedAt.ToUniversalTime().ShouldBe(Now);
    }
}
