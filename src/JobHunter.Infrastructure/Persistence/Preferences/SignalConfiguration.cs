using JobHunter.Domain.Jobs;
using JobHunter.Domain.Preferences;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Preferences;

/// <summary>
/// Maps the <see cref="Signal"/> entity to <c>signals</c> (F7 data-model §signals). F5 and F6 write rows;
/// F7 owns the schema. The unique <c>uq_signals_action</c> on <c>(job_id, kind, occurred_at)</c> is what
/// makes capture idempotent — a redelivered card action produces no second signal. <c>idx_signals_window</c>
/// serves the 180-day fitting window and <c>idx_signals_kind</c> the per-kind aggregation. <c>job_facts</c>
/// is the load-bearing snapshot and persists as <c>jsonb</c> through <see cref="JobFactsJson"/>; it is never
/// a join to <c>jobs</c>, so a later edit cannot rewrite what the Owner reacted to.
/// </summary>
internal sealed class SignalConfiguration : IEntityTypeConfiguration<Signal>
{
    public void Configure(EntityTypeBuilder<Signal> b)
    {
        b.ToTable("signals");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
        b.Property(x => x.ApplicationId).HasColumnName("application_id");
        b.Property(x => x.Kind).HasColumnName("kind").IsRequired();
        b.Property(x => x.Weight).HasColumnName("weight").HasColumnType("numeric(3,1)").IsRequired();
        b.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();

        b.Property(x => x.JobFacts)
            .HasColumnName("job_facts")
            .HasColumnType("jsonb")
            .HasConversion(v => JobFactsJson.Serialize(v), v => JobFactsJson.Deserialize(v))
            .IsRequired();

        b.HasOne<Job>()
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.JobId, x.Kind, x.OccurredAt })
            .IsUnique()
            .HasDatabaseName("uq_signals_action");
        b.HasIndex(x => x.OccurredAt).HasDatabaseName("idx_signals_window").IsDescending();
        b.HasIndex(x => new { x.Kind, x.OccurredAt })
            .HasDatabaseName("idx_signals_kind")
            .IsDescending(false, true);
    }
}
