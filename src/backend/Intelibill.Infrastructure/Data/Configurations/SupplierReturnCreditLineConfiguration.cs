using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SupplierReturnCreditLineConfiguration : IEntityTypeConfiguration<SupplierReturnCreditLine>
{
    public void Configure(EntityTypeBuilder<SupplierReturnCreditLine> b)
    {
        b.ToTable("supplier_return_credit_lines", t=>t.HasCheckConstraint("ck_supplier_return_credit_lines_amounts", "accepted_amount >= 0 AND ((accepted_amount = 0 AND supplier_ledger_entry_id IS NULL) OR (accepted_amount > 0 AND supplier_ledger_entry_id IS NOT NULL))"));
        b.HasKey(x=>x.Id);
        b.HasAlternateKey(x=>new{x.Id,x.ShopId});
        b.HasOne<Shop>().WithMany().HasForeignKey(x=>x.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x=>x.AcceptedAmount).HasPrecision(10,2);
        b.HasIndex(x=>x.ReturnLineId).IsUnique();
        b.HasOne<SupplierReturnLine>().WithMany().HasPrincipalKey(x=>new{x.Id,x.ShopId}).HasForeignKey(x=>new{x.ReturnLineId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PurchaseReceiptValue>().WithMany().HasPrincipalKey(x=>new{x.Id,x.ShopId}).HasForeignKey(x=>new{x.ReceiptValueId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SupplierLedgerEntry>().WithMany().HasPrincipalKey(x=>new{x.Id,x.ShopId}).HasForeignKey(x=>new{x.SupplierLedgerEntryId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
    }
}
