using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T03: the CV version repository's upload-time operations against a real database. <c>NextVersionAsync</c>
/// yields a monotonic, gap-free version per profile; <c>ActivateAsync</c> deactivates the previous active
/// version and inserts the new one in one transaction, so <c>uq_cv_versions_active</c> never sees two
/// active rows — the exact race the partial unique index would otherwise reject. The previous version is
/// deactivated, not deleted: it stays as the honest record of what earlier matches were made against.
/// Requires Docker.
/// </summary>
public sealed class CvVersionActivationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Next_version_starts_at_one_and_increments_per_profile()
    {
        await using var database = await TestDatabase.CreateAsync();
        var profile = NewProfile();
        await SeedProfileAsync(database, profile);

        var repo = new CvVersionRepository(database.CreateContext());
        (await repo.NextVersionAsync(profile.Id)).ShouldBe((short)1);

        repo.Add(NewCv(profile.Id, version: 1, isActive: true, hash: new string('a', 64)));
        await repo.SaveChangesAsync();

        var next = new CvVersionRepository(database.CreateContext());
        (await next.NextVersionAsync(profile.Id)).ShouldBe((short)2);
    }

    [RequiresDockerFact]
    public async Task Activating_a_new_version_deactivates_the_previous_one_without_deleting_it()
    {
        await using var database = await TestDatabase.CreateAsync();
        var profile = NewProfile();
        await SeedProfileAsync(database, profile);

        var first = new CvVersionRepository(database.CreateContext());
        first.Add(NewCv(profile.Id, version: 1, isActive: true, hash: new string('a', 64)));
        await first.SaveChangesAsync();

        // Activating v2 must clear v1's active flag inside the same transaction, or uq_cv_versions_active
        // would reject the second active row.
        var activator = new CvVersionRepository(database.CreateContext());
        await activator.ActivateAsync(NewCv(profile.Id, version: 2, isActive: true, hash: new string('b', 64)));

        await using var read = database.CreateContext();
        var versions = await read.Set<CvVersion>()
            .Where(v => v.ProfileId == profile.Id)
            .OrderBy(v => v.Version)
            .ToListAsync();

        versions.Count.ShouldBe(2);
        versions[0].Version.ShouldBe((short)1);
        versions[0].IsActive.ShouldBeFalse();
        versions[1].Version.ShouldBe((short)2);
        versions[1].IsActive.ShouldBeTrue();
    }

    [RequiresDockerFact]
    public async Task Activating_the_first_version_needs_no_previous_to_deactivate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var profile = NewProfile();
        await SeedProfileAsync(database, profile);

        var activator = new CvVersionRepository(database.CreateContext());
        await activator.ActivateAsync(NewCv(profile.Id, version: 1, isActive: true, hash: new string('a', 64)));

        await using var read = database.CreateContext();
        var stored = await read.Set<CvVersion>().SingleAsync();
        stored.IsActive.ShouldBeTrue();
        stored.Version.ShouldBe((short)1);
    }

    [RequiresDockerFact]
    public async Task No_binary_column_exists_anywhere_to_persist_the_uploaded_cv()
    {
        // The column scan behind "the uploaded binary is not persisted anywhere" (T03 Done-when): the CV's
        // bytes are extracted in memory and discarded, so the schema offers no bytea column to hold them.
        await using var database = await TestDatabase.CreateAsync();

        await using var connection = new Npgsql.NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT table_name || '.' || column_name FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND data_type = 'bytea'";

        var binaryColumns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            binaryColumns.Add(reader.GetString(0));
        }

        binaryColumns.ShouldBeEmpty();
    }

    private static Profile NewProfile() =>
        new(
            Guid.CreateVersion7(), isActive: true, "Owner", salaryFloor: null, salaryFloorCurrency: null,
            TimezoneBand.EMEA, preferredCountries: ["Germany"], employmentTypes: [EmploymentType.FullTime], Now);

    private static CvVersion NewCv(Guid profileId, short version, bool isActive, string hash) =>
        new(
            Guid.CreateVersion7(), profileId, version, isActive, "cv.pdf", "application/pdf",
            sizeBytes: 1024, contentHash: hash, extractedText: "Extracted CV text.",
            uploadedAt: Now, activatedAt: isActive ? Now : null);

    private static async Task SeedProfileAsync(TestDatabase database, Profile profile)
    {
        await using var ctx = database.CreateContext();
        ctx.Add(profile);
        await ctx.SaveChangesAsync();
    }
}
