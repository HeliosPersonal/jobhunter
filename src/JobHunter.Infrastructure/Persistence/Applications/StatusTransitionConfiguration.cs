using JobHunter.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Applications;

/// <summary>
/// Maps the <see cref="StatusTransition"/> history row to <c>application_transitions</c> (F6 data-model
/// §application_transitions). <c>idx_transitions_application</c> on <c>(application_id, occurred_at)</c>
/// serves the history view (AC-03); the partial <c>idx_transitions_outcome</c> on
/// <c>(to_status, occurred_at)</c> — filtered to the outcome statuses — serves the outcome signals and
/// conversion metrics F7 reads. <c>from_status</c> is nullable for the creating transition. The table is
/// append-only (QG-1): nothing here permits an update or a delete.
/// </summary>
internal sealed class StatusTransitionConfiguration : IEntityTypeConfiguration<StatusTransition>
{
    public void Configure(EntityTypeBuilder<StatusTransition> b)
    {
        b.ToTable("application_transitions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.ApplicationId).HasColumnName("application_id").IsRequired();
        b.Property(x => x.From).HasColumnName("from_status");
        b.Property(x => x.To).HasColumnName("to_status").IsRequired();
        b.Property(x => x.Source).HasColumnName("source").IsRequired();
        b.Property(x => x.Detail).HasColumnName("detail");
        b.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();

        b.HasIndex(x => new { x.ApplicationId, x.OccurredAt })
            .HasDatabaseName("idx_transitions_application");
        b.HasIndex(x => new { x.To, x.OccurredAt })
            .HasDatabaseName("idx_transitions_outcome")
            .HasFilter("to_status IN ('Interview','Offer','Rejected')");
    }
}
