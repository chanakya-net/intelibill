using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class InvoiceLeaseConfiguration : IEntityTypeConfiguration<InvoiceLease>
{
    public void Configure(EntityTypeBuilder<InvoiceLease> builder)
    {
        builder.ToTable("invoice_leases");

        builder.HasKey(lease => lease.Id);

        builder.Property(lease => lease.ShopId)
            .IsRequired();

        builder.Property(lease => lease.InvoiceSequenceId)
            .IsRequired();

        builder.Property(lease => lease.FiscalYearStart)
            .IsRequired();

        builder.Property(lease => lease.DeviceId)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(lease => lease.Prefix)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(lease => lease.RangeStart)
            .IsRequired();

        builder.Property(lease => lease.RangeEnd)
            .IsRequired();

        builder.Property(lease => lease.NextNumber)
            .IsRequired();

        builder.Property(lease => lease.NumberPadding)
            .IsRequired();

        builder.Property(lease => lease.ReservedAt)
            .IsRequired();

        builder.Property(lease => lease.ExpiresAt)
            .IsRequired();

        builder.HasIndex(lease => new { lease.ShopId, lease.DeviceId, lease.FiscalYearStart });
        builder.HasIndex(lease => new { lease.ShopId, lease.ExpiresAt });

        builder.HasOne(lease => lease.InvoiceSequence)
            .WithMany()
            .HasForeignKey(lease => lease.InvoiceSequenceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(lease => lease.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
