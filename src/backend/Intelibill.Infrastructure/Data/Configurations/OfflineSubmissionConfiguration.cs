using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class OfflineSubmissionConfiguration : IEntityTypeConfiguration<OfflineSubmission>
{
    public void Configure(EntityTypeBuilder<OfflineSubmission> builder)
    {
        builder.ToTable("offline_submissions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ShopId).IsRequired();

        builder.Property(s => s.DeviceId)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(s => s.ClientSaleId)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(s => s.InvoiceNumber)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(s => s.PayloadHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(s => s.PayloadJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(s => s.ClaimedSoldAt).IsRequired();
        builder.Property(s => s.ReceivedAt).IsRequired();
        builder.Property(s => s.Source).IsRequired();
        builder.Property(s => s.SubmittedByUserId).IsRequired();

        builder.Property(s => s.RecoveryOperationId)
            .HasMaxLength(128);

        builder.HasIndex(s => new { s.ShopId, s.DeviceId, s.ClientSaleId, s.PayloadHash })
            .IsUnique();
        builder.HasIndex(s => new { s.ShopId, s.DeviceId, s.SubmittedByUserId });
        builder.HasIndex(s => s.SaleId);

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(s => s.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(s => s.SaleId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
