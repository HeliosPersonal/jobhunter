using JobHunter.Domain.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Pipeline;

/// <summary>
/// Maps the <see cref="Run"/> aggregate to <c>runs</c> (data-model §runs). Two indexes carry
/// invariants: <c>idx_runs_resumable</c> serves "non-terminal Runs to resume on startup" (the whole of
/// QG-1), and <c>uq_runs_single_active</c> — a partial unique index over a constant expression filtered
/// to the non-terminal states — is what makes two Runs racing after a botched restart impossible rather
/// than merely unlikely. Both partial indexes are declared in raw SQL in the migration, because EF
/// cannot model a unique index over a constant expression; they are named here for documentation only.
/// </summary>
internal sealed class RunConfiguration : IEntityTypeConfiguration<Run>
{
    public void Configure(EntityTypeBuilder<Run> b)
    {
        b.ToTable("runs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.State).HasColumnName("state").IsRequired();
        b.Property(x => x.CutoffFrom).HasColumnName("cutoff_from").IsRequired();
        b.Property(x => x.CutoffTo).HasColumnName("cutoff_to").IsRequired();
        b.Property(x => x.CeilingUsd).HasColumnName("ceiling_usd").HasColumnType("numeric(8,4)").IsRequired();
        b.Property(x => x.SpentUsd).HasColumnName("spent_usd").HasColumnType("numeric(8,4)").HasDefaultValue(0m).IsRequired();
        b.Property(x => x.JobsInScope).HasColumnName("jobs_in_scope").HasDefaultValue(0).IsRequired();
        b.Property(x => x.JobsCarriedOver).HasColumnName("jobs_carried_over").HasDefaultValue(0).IsRequired();
        b.Property(x => x.StartedAt).HasColumnName("started_at");
        b.Property(x => x.FinishedAt).HasColumnName("finished_at");
        b.Property(x => x.FailureReason).HasColumnName("failure_reason");

        b.HasIndex(x => x.FinishedAt)
            .HasDatabaseName("idx_runs_delivered")
            .HasFilter("state = 'Delivered'");
    }
}
