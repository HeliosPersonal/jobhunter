using JobHunter.Domain.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Commands;

/// <summary>
/// Maps <see cref="CommandInvocation"/> to <c>command_invocations</c> (F10 data-model §command_invocations),
/// the only table F10 owns. It has no foreign key — F10 is a surface, and an audit row must survive whatever
/// it references. <c>idx_command_invocations_command</c> serves the per-command usage metric ([[PRD]] §7),
/// <c>idx_command_invocations_outcome</c> the unknown/throttled rates, and <c>idx_command_invocations_time</c>
/// the 180-day retention prune. The table is append-only — the repository exposes no update or delete — and
/// this mapping exists so the migration creates the table and its indexes. There is deliberately no column
/// for argument content: <see cref="CommandInvocation.ArgCount"/> is the count and nothing more (SAD §8).
/// </summary>
internal sealed class CommandInvocationConfiguration : IEntityTypeConfiguration<CommandInvocation>
{
    public void Configure(EntityTypeBuilder<CommandInvocation> b)
    {
        b.ToTable("command_invocations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.ChatId).HasColumnName("chat_id").IsRequired();
        b.Property(x => x.Command).HasColumnName("command").IsRequired();
        b.Property(x => x.Outcome).HasColumnName("outcome").IsRequired();
        b.Property(x => x.DurationMs).HasColumnName("duration_ms").IsRequired();
        b.Property(x => x.ArgCount).HasColumnName("arg_count").HasColumnType("smallint").IsRequired();
        b.Property(x => x.InvokedAt).HasColumnName("invoked_at").IsRequired();

        b.HasIndex(x => new { x.Command, x.InvokedAt })
            .HasDatabaseName("idx_command_invocations_command")
            .IsDescending(false, true);
        b.HasIndex(x => new { x.Outcome, x.InvokedAt })
            .HasDatabaseName("idx_command_invocations_outcome")
            .IsDescending(false, true);
        b.HasIndex(x => x.InvokedAt).HasDatabaseName("idx_command_invocations_time");
    }
}
