using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class PurchaseReceiptReversalConfiguration : IEntityTypeConfiguration<PurchaseReceiptReversal>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptReversal> b)
    {
        b.ToTable("purchase_receipt_reversals", t=>t.HasCheckConstraint("ck_purchase_receipt_reversals_amounts", "quantity > 0 AND effective_amount >= 0 AND ((effective_amount = 0 AND supplier_ledger_entry_id IS NULL) OR (effective_amount > 0 AND supplier_ledger_entry_id IS NOT NULL))"));
        b.HasKey(x=>x.Id);
        b.HasAlternateKey(x=>new{x.Id,x.ShopId});
        b.HasOne<Shop>().WithMany().HasForeignKey(x=>x.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x=>x.Reason).HasMaxLength(255);
        b.Property(x=>x.Reference).HasMaxLength(80);
        b.Property(x=>x.BusinessTimeZoneId).HasMaxLength(80);
        b.Property(x=>x.Quantity).HasPrecision(18,3);
        b.Property(x=>x.EffectiveAmount).HasPrecision(10,2);
        b.HasIndex(x=>x.ReceiptValueId).IsUnique();
        b.HasOne<PurchaseReceiptValue>().WithMany().HasPrincipalKey(x=>new{x.Id,x.ShopId}).HasForeignKey(x=>new{x.ReceiptValueId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockTransaction>().WithMany().HasPrincipalKey(x=>new{x.Id,x.ShopId}).HasForeignKey(x=>new{x.StockTransactionId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SupplierLedgerEntry>().WithMany().HasPrincipalKey(x=>new{x.Id,x.ShopId}).HasForeignKey(x=>new{x.SupplierLedgerEntryId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
    }
}
