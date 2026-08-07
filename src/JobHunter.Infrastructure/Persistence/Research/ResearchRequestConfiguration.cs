using JobHunter.Domain.Companies;
using JobHunter.Domain.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Research;

/// <summary>
/// Maps the <see cref="ResearchRequest"/> to <c>research_requests</c> (F8 T09, SAD §6.2). The surrogate
/// <c>id</c> is the identity, but the table's working key is the partial unique index
/// <c>uq_research_requests_open</c> on <c>(company_id) WHERE NOT consumed</c>: at most one open request per
/// company, so a second <c>/company</c> before the next cycle drains the queue is an idempotent no-op. Once
/// <c>consumed</c> flips the row is history, kept for audit rather than deleted.
/// </summary>
internal sealed class ResearchRequestConfiguration : IEntityTypeConfiguration<ResearchRequest>
{
    public void Configure(EntityTypeBuilder<ResearchRequest> b)
    {
        b.ToTable("research_requests");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.Reason).HasColumnName("reason").IsRequired();
        b.Property(x => x.RequestedAt).HasColumnName("requested_at").IsRequired();
        b.Property(x => x.Consumed).HasColumnName("consumed").IsRequired();

        b.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // At most one open request per company: the enqueue upsert relies on this partial unique index to make
        // a repeat enqueue an idempotent no-op (SAD §6.2, AC-05).
        b.HasIndex(x => x.CompanyId)
            .HasDatabaseName("uq_research_requests_open")
            .HasFilter("NOT consumed")
            .IsUnique();
    }
}
