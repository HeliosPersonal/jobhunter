using JobHunter.Domain.Companies;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Npgsql;
using Shouldly;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F8 T09 C3: the write side of the on-demand <c>/company</c> command (SAD §6.2, AC-05). When the Owner asks
/// about a company whose dossier is stale or absent, the request is queued for the next research cycle rather
/// than run inline. <see cref="ResearchRequestWriter"/> inserts one <c>research_requests</c> row with
/// <c>ON CONFLICT DO NOTHING</c> against the partial unique index on <c>(company_id) WHERE NOT consumed</c>, so
/// asking about the same company twice before the cycle drains the queue enqueues it once (idempotent per
/// company per pending cycle). The id and timestamp come from the injected <see cref="IIdGenerator"/> and
/// <see cref="IClock"/> so the write is deterministic. It stores nothing about the Owner's CV. Requires Docker.
/// </summary>
public sealed class ResearchRequestWriterTests
{
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 6, 0, 0, TimeSpan.Zero);

    private const string CountSql =
        "SELECT count(*) FROM research_requests WHERE company_id = @c AND NOT consumed;";

    [RequiresDockerFact]
    public async Task Enqueue_writes_a_pending_request()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database, "Acme AI");

        var writer = NewWriter(database);
        await writer.EnqueueAsync(companyId, "on-demand /company");

        (await PendingCountAsync(database, companyId)).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task Enqueuing_the_same_company_twice_in_a_cycle_queues_it_once()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database, "Acme AI");

        var writer = NewWriter(database);
        await writer.EnqueueAsync(companyId, "on-demand /company");
        await writer.EnqueueAsync(companyId, "on-demand /company");

        (await PendingCountAsync(database, companyId)).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task A_second_request_after_the_first_is_consumed_is_queued_again()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database, "Acme AI");

        var writer = NewWriter(database);
        await writer.EnqueueAsync(companyId, "on-demand /company");
        await ConsumeAllAsync(database);
        await writer.EnqueueAsync(companyId, "on-demand /company");

        (await PendingCountAsync(database, companyId)).ShouldBe(1);
    }

    private static ResearchRequestWriter NewWriter(TestDatabase database) =>
        new(new NpgsqlConnectionFactory(database.ConnectionString), new SequentialIdGenerator(), new FakeClock(Now));

    private static async Task<long> PendingCountAsync(TestDatabase database, Guid companyId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(CountSql, connection);
        command.Parameters.AddWithValue("c", companyId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ConsumeAllAsync(TestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE research_requests SET consumed = true;", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> SeedCompanyAsync(TestDatabase database, string name)
    {
        var companyId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, name, CompanySource.Curated, RunStart));
        await ctx.SaveChangesAsync();
        return companyId;
    }
}
