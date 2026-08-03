using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T02: the registry tables round-trip through EF Core on a freshly-migrated database, value objects
/// (canonical domain, confidence) map to their columns, enums persist as text, and the live-binding
/// unique index is enforced. Requires Docker.
/// </summary>
public sealed class RegistryPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 7, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Company_and_binding_round_trip_through_the_repository()
    {
        await using var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var domain = CanonicalDomain.TryCreate("stripe.com").Value;

        await using (var write = database.CreateContext())
        {
            var repo = new CompanyRepository(write);
            await repo.AddAsync(new Company(companyId, domain, "Stripe", CompanySource.Curated, Now, "https://stripe.com/jobs", "US"));
            await repo.AddBindingAsync(new AtsBinding(
                bindingId, companyId, AtsKind.Greenhouse, "stripe", BindingConfidence.TryCreate(0.95m).Value, "{\"probe\":\"ok\"}", Now));
            await repo.SaveChangesAsync();
        }

        await using (var read = database.CreateContext())
        {
            var repo = new CompanyRepository(read);
            var company = await repo.FindByDomainAsync(domain);
            company.ShouldNotBeNull();
            company!.DisplayName.ShouldBe("Stripe");
            company.CanonicalDomain.Value.ShouldBe("stripe.com");

            var bindings = await repo.LiveBindingsAsync(companyId);
            bindings.Count.ShouldBe(1);
            bindings[0].AtsKind.ShouldBe(AtsKind.Greenhouse);
            bindings[0].Confidence.Value.ShouldBe(0.95m);
        }
    }

    [RequiresDockerFact]
    public async Task Comp_band_and_remote_emea_round_trip_and_band_persists_as_text()
    {
        await using var database = await TestDatabase.CreateAsync();
        var taggedId = Guid.CreateVersion7();
        var untaggedId = Guid.CreateVersion7();

        await using (var write = database.CreateContext())
        {
            var repo = new CompanyRepository(write);
            await repo.AddAsync(new Company(
                taggedId, CanonicalDomain.TryCreate("tagged.com").Value, "Tagged", CompanySource.Curated, Now,
                compBand: CompBand.Top, remoteEmeaFriendly: true));
            // An untagged company still persists — the columns are nullable and advisory.
            await repo.AddAsync(new Company(
                untaggedId, CanonicalDomain.TryCreate("untagged.com").Value, "Untagged", CompanySource.Curated, Now));
            await repo.SaveChangesAsync();
        }

        await using (var read = database.CreateContext())
        {
            var repo = new CompanyRepository(read);
            var tagged = await repo.FindByDomainAsync(CanonicalDomain.TryCreate("tagged.com").Value);
            tagged!.CompBand.ShouldBe(CompBand.Top);
            tagged.RemoteEmeaFriendly.ShouldBe(true);

            var untagged = await repo.FindByDomainAsync(CanonicalDomain.TryCreate("untagged.com").Value);
            untagged!.CompBand.ShouldBeNull();
            untagged.RemoteEmeaFriendly.ShouldBeNull();
        }

        await using var connection = new Npgsql.NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT comp_band FROM companies WHERE id = @id";
        command.Parameters.AddWithValue("id", taggedId);
        var stored = (string?)await command.ExecuteScalarAsync();
        stored.ShouldBe("Top");
    }

    [RequiresDockerFact]
    public async Task Confidence_persists_as_numeric_and_ats_kind_as_text()
    {
        await using var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();

        await using (var write = database.CreateContext())
        {
            var repo = new CompanyRepository(write);
            await repo.AddAsync(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Manual, Now));
            await repo.AddBindingAsync(new AtsBinding(
                bindingId, companyId, AtsKind.Lever, "acme", BindingConfidence.TryCreate(0.8m).Value, "{}", Now));
            await repo.SaveChangesAsync();
        }

        await using var connection = new Npgsql.NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ats_kind, confidence::text FROM ats_bindings WHERE id = @id";
        command.Parameters.AddWithValue("id", bindingId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("Lever");
        reader.GetString(1).ShouldBe("0.80");
    }

    [RequiresDockerFact]
    public async Task A_second_live_binding_for_the_same_provider_violates_the_unique_index()
    {
        await using var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();

        await using (var seed = database.CreateContext())
        {
            var repo = new CompanyRepository(seed);
            await repo.AddAsync(new Company(companyId, CanonicalDomain.TryCreate("dup.com").Value, "Dup", CompanySource.Curated, Now));
            await repo.AddBindingAsync(new AtsBinding(
                Guid.CreateVersion7(), companyId, AtsKind.Ashby, "dup", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
            await repo.SaveChangesAsync();
        }

        await using var clash = database.CreateContext();
        var clashRepo = new CompanyRepository(clash);
        await clashRepo.AddBindingAsync(new AtsBinding(
            Guid.CreateVersion7(), companyId, AtsKind.Ashby, "dup", BindingConfidence.TryCreate(0.85m).Value, "{}", Now));

        await Should.ThrowAsync<DbUpdateException>(() => clashRepo.SaveChangesAsync());
    }

    [RequiresDockerFact]
    public async Task A_retired_binding_frees_the_unique_slot_for_a_new_one()
    {
        await using var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var firstBindingId = Guid.CreateVersion7();

        await using (var seed = database.CreateContext())
        {
            var repo = new CompanyRepository(seed);
            await repo.AddAsync(new Company(companyId, CanonicalDomain.TryCreate("migrate.com").Value, "Migrate", CompanySource.Curated, Now));
            await repo.AddBindingAsync(new AtsBinding(
                firstBindingId, companyId, AtsKind.Workable, "migrate", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
            await repo.SaveChangesAsync();
        }

        await using (var migrate = database.CreateContext())
        {
            var binding = await migrate.Set<AtsBinding>().SingleAsync(x => x.Id == firstBindingId);
            binding.Retire(new FakeClock(Now));
            await migrate.SaveChangesAsync();

            migrate.Add(new AtsBinding(
                Guid.CreateVersion7(), companyId, AtsKind.Workable, "migrate", BindingConfidence.TryCreate(0.92m).Value, "{}", Now));
            await migrate.SaveChangesAsync();
        }

        await using var read = database.CreateContext();
        var live = await read.Set<AtsBinding>().CountAsync(x => x.CompanyId == companyId && x.RetiredAt == null);
        live.ShouldBe(1);
        var total = await read.Set<AtsBinding>().CountAsync(x => x.CompanyId == companyId);
        total.ShouldBe(2);
    }
}
