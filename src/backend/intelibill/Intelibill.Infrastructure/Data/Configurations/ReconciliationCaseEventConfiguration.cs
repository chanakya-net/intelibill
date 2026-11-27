using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ReconciliationCaseEventConfiguration : IEntityTypeConfiguration<ReconciliationCaseEvent>
{
    public void Configure(EntityTypeBuilder<ReconciliationCaseEvent> builder)
    {
        builder.ToTable("reconciliation_case_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ShopId).IsRequired();
        builder.Property(e => e.CaseId).IsRequired();
        builder.Property(e => e.Action).IsRequired();
        builder.Property(e => e.FromStatus).IsRequired();
        builder.Property(e => e.ToStatus).IsRequired();
        builder.Property(e => e.ActorUserId).IsRequired();

        builder.Property(e => e.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.OperationKey)
            .HasMaxLength(128);

        builder.Property(e => e.OccurredAt).IsRequired();

        builder.HasIndex(e => new { e.ShopId, e.CaseId, e.OccurredAt });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(e => e.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ReconciliationIssue>()
            .WithMany()
            .HasForeignKey(e => e.CaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
