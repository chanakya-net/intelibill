using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SupplierReturnConfiguration : IEntityTypeConfiguration<SupplierReturn>
{
    public void Configure(EntityTypeBuilder<SupplierReturn> b)
    {
        b.ToTable("supplier_returns");
        b.HasKey(x=>x.Id);
        b.HasAlternateKey(x=>new{x.Id,x.ShopId});
        b.HasOne<Shop>().WithMany().HasForeignKey(x=>x.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x=>x.ReturnNumber).HasMaxLength(80);
        b.Property(x=>x.Reason).HasMaxLength(255);
        b.Property(x=>x.ShopName).HasMaxLength(255);
        b.Property(x=>x.SupplierName).HasMaxLength(255);
        b.Property(x=>x.BusinessTimeZoneId).HasMaxLength(80);
        b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(32);
        b.HasIndex(x=>new {x.ShopId,x.ReturnNumber}).IsUnique();
        b.HasIndex(x=>new {x.ShopId,x.SupplierId,x.ShippedAt});
        b.HasOne<Supplier>().WithMany().HasPrincipalKey(x=>new{x.Id,x.ShopId}).HasForeignKey(x=>new{x.SupplierId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x=>x.Lines).WithOne().HasPrincipalKey(x=>new{x.Id,x.ShopId}).HasForeignKey(x=>new{x.SupplierReturnId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.Credit).WithOne().HasPrincipalKey<SupplierReturn>(x=>new{x.Id,x.ShopId}).HasForeignKey<SupplierReturnCredit>(x=>new{x.SupplierReturnId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
    }
}
