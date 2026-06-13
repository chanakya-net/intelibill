using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class CreditNoteRedemptionConfiguration : IEntityTypeConfiguration<CreditNoteRedemption>
{
    public void Configure(EntityTypeBuilder<CreditNoteRedemption> builder)
    {
        builder.ToTable("credit_note_redemptions", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_credit_note_redemptions_amount_positive", "amount > 0");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.CreditNoteId).IsRequired();
        builder.Property(r => r.ShopId).IsRequired();
        builder.Property(r => r.SaleId).IsRequired();

        builder.Property(r => r.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.HasIndex(r => r.CreditNoteId);
        builder.HasIndex(r => new { r.ShopId, r.SaleId });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(r => r.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(r => r.SaleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
