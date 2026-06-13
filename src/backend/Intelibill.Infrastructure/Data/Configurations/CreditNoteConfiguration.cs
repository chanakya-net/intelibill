using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("credit_notes", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_credit_notes_original_amount_positive", "original_amount > 0");
            tableBuilder.HasCheckConstraint("ck_credit_notes_available_balance_non_negative", "available_balance >= 0");
            tableBuilder.HasCheckConstraint("ck_credit_notes_balance_le_original", "available_balance <= original_amount");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ShopId).IsRequired();
        builder.Property(c => c.SaleReturnId).IsRequired();

        builder.Property(c => c.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(c => c.OriginalAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(c => c.AvailableBalance)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(c => c.Reason)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(c => c.IsVoided)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasIndex(c => new { c.ShopId, c.Code }).IsUnique();
        builder.HasIndex(c => new { c.ShopId, c.SaleReturnId });
        builder.HasIndex(c => new { c.ShopId, c.IsVoided });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(c => c.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<SaleReturn>()
            .WithMany()
            .HasForeignKey(c => c.SaleReturnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Redemptions)
            .WithOne()
            .HasForeignKey(r => r.CreditNoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
