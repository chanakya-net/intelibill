using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class InvoiceSequenceConfiguration : IEntityTypeConfiguration<InvoiceSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceSequence> builder)
    {
        builder.ToTable("invoice_sequences");

        builder.HasKey(sequence => sequence.Id);

        builder.Property(sequence => sequence.ShopId)
            .IsRequired();

        builder.Property(sequence => sequence.FiscalYearStart)
            .IsRequired();

        builder.Property(sequence => sequence.NextNumber)
            .IsRequired();

        builder.Property(sequence => sequence.Prefix)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(sequence => new { sequence.ShopId, sequence.FiscalYearStart })
            .IsUnique();

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(sequence => sequence.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
