using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Data.Configurations;

internal sealed class CatalogueDraftChangeConfiguration : IEntityTypeConfiguration<CatalogueDraftChange>
{
    public void Configure(EntityTypeBuilder<CatalogueDraftChange> builder)
    {
        builder.ToTable("catalogue_draft_changes", table =>
        {
            table.HasCheckConstraint("ck_catalogue_draft_changes_target", "target_kind IN (1, 2)");
            table.HasCheckConstraint("ck_catalogue_draft_changes_revision", "revision >= 1");
            table.HasCheckConstraint("ck_catalogue_draft_changes_reason",
                "reason = btrim(reason) AND char_length(reason) BETWEEN 1 AND 500");
        });
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();
        builder.Property(change => change.Reason).HasMaxLength(500);
        builder.HasOne<AdminIdentity>().WithMany().HasForeignKey(change => change.ActorAdminId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(change => new { change.TargetKind, change.TargetId, change.Revision }).IsUnique();
    }
}
