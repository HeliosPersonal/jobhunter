using JobHunter.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Infrastructure.Persistence.Applications;

/// <summary>
/// Maps the <see cref="App"/> aggregate to <c>applications</c> (F6 data-model §applications). The unique
/// <c>uq_applications_job</c> on <c>job_id</c> is the "one application per job" constraint. Two partial
/// indexes serve the two hot reads: <c>idx_applications_pipeline</c> on <c>(status, last_activity_at DESC)</c>
/// for the pipeline view (AC-01) and <c>idx_applications_due</c> on <c>next_action_at</c> for the reminder
/// sweep — both filtered to <c>NOT archived</c> so archived rows never widen the scan. The transitions and
/// notes are owned children written through the same insert, each ordered by its own occurrence time.
/// </summary>
internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<App>
{
    public void Configure(EntityTypeBuilder<App> b)
    {
        b.ToTable("applications");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
        b.Property(x => x.Status).HasColumnName("status").IsRequired();
        b.Property(x => x.PostingClosed).HasColumnName("posting_closed").HasDefaultValue(false).IsRequired();
        b.Property(x => x.Archived).HasColumnName("archived").HasDefaultValue(false).IsRequired();
        b.Property(x => x.AppliedAt).HasColumnName("applied_at");
        b.Property(x => x.LastActivityAt).HasColumnName("last_activity_at").IsRequired();
        b.Property(x => x.NextActionAt).HasColumnName("next_action_at");
        b.Property(x => x.LastReminderCondition).HasColumnName("last_reminder_condition");
        b.Property(x => x.LastReminderAt).HasColumnName("last_reminder_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasOne<Job>()
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Transitions)
            .WithOne()
            .HasForeignKey(t => t.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Transitions).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasMany(x => x.Notes)
            .WithOne()
            .HasForeignKey(n => n.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Notes).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.JobId).IsUnique().HasDatabaseName("uq_applications_job");
        b.HasIndex(x => new { x.Status, x.LastActivityAt })
            .HasDatabaseName("idx_applications_pipeline")
            .IsDescending(false, true)
            .HasFilter("NOT archived");
        b.HasIndex(x => x.NextActionAt)
            .HasDatabaseName("idx_applications_due")
            .HasFilter("next_action_at IS NOT NULL AND NOT archived");
    }
}
