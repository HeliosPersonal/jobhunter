using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence;

/// <summary>
/// The single EF Core write context. It owns the <c>public</c> schema, its migrations and every
/// aggregate write (ADR-0003). Read models go through Dapper, never through here. Configurations are
/// discovered by assembly scan so a later feature adds a table by adding one
/// <c>internal sealed IEntityTypeConfiguration&lt;T&gt;</c> and one migration — no edit to this file.
/// </summary>
public sealed class JobHunterDbContext(DbContextOptions<JobHunterDbContext> options) : DbContext(options)
{
    /// <summary>The schema Hangfire owns (ADR-0004); created by the first migration.</summary>
    public const string HangfireSchema = "hangfire";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Enums persist as `text`, never as ordinals (coding-standards §5). Applied as a convention so
        // no per-property configuration can forget it; a test asserts no enum maps to an integer column.
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobHunterDbContext).Assembly);
    }
}
