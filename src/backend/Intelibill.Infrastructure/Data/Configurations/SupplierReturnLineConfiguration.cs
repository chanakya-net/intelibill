using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SupplierReturnLineConfiguration : IEntityTypeConfiguration<SupplierReturnLine>
{
    public void Configure(EntityTypeBuilder<SupplierReturnLine> b)
    {
        b.ToTable("supplier_return_lines", t=>t.HasCheckConstraint("ck_supplier_return_lines_amounts", "quantity > 0 AND quantity <= received_quantity AND proposed_credit_amount >= 0 AND effective_total_cost >= 0"));
        b.HasKey(x=>x.Id);
        b.HasAlternateKey(x=>new{x.Id,x.ShopId});
        b.HasOne<Shop>().WithMany().HasForeignKey(x=>x.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x=>x.Quantity).HasPrecision(18,3);
        b.Property(x=>x.ProposedCreditAmount).HasPrecision(10,2);
        b.Property(x=>x.ReceivedQuantity).HasPrecision(18,3);
        b.Property(x=>x.EffectiveTotalCost).HasPrecision(10,2);
        b.Property(x=>x.ItemName).HasMaxLength(255);
        b.Property(x=>x.Uqc).HasMaxLength(80);
        b.Property(x=>x.SourceReference).HasMaxLength(80);
        b.HasIndex(x=>new{x.SupplierReturnId,x.ReceiptValueId}).IsUnique();
        b.HasOne<PurchaseReceiptValue>().WithMany().HasPrincipalKey(x=>new{x.Id,x.ShopId}).HasForeignKey(x=>new{x.ReceiptValueId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryBatch>().WithMany().HasPrincipalKey(x=>new{x.Id,x.ItemId,x.ShopId}).HasForeignKey(x=>new{x.InventoryBatchId,x.ItemId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockTransaction>().WithMany().HasPrincipalKey(x=>new{x.Id,x.ShopId}).HasForeignKey(x=>new{x.StockTransactionId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
    }
}
