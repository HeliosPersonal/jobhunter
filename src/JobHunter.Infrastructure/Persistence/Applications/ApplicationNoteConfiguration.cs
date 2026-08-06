using JobHunter.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Applications;

/// <summary>
/// Maps the <see cref="ApplicationNote"/> to <c>application_notes</c> (F6 data-model §application_notes).
/// <c>idx_notes_application</c> on <c>(application_id, created_at DESC)</c> serves the notes shown in the
/// history view, newest first. The body is capped at <see cref="ApplicationNote.MaxLength"/> by the
/// aggregate and never logged — only its length is (invariant 12).
/// </summary>
internal sealed class ApplicationNoteConfiguration : IEntityTypeConfiguration<ApplicationNote>
{
    public void Configure(EntityTypeBuilder<ApplicationNote> b)
    {
        b.ToTable("application_notes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.ApplicationId).HasColumnName("application_id").IsRequired();
        b.Property(x => x.Body).HasColumnName("body").HasMaxLength(ApplicationNote.MaxLength).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasIndex(x => new { x.ApplicationId, x.CreatedAt })
            .HasDatabaseName("idx_notes_application")
            .IsDescending(false, true);
    }
}
