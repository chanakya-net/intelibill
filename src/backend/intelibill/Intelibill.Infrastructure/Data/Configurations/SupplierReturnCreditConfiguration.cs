using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SupplierReturnCreditConfiguration : IEntityTypeConfiguration<SupplierReturnCredit>
{
    public void Configure(EntityTypeBuilder<SupplierReturnCredit> b)
    {
        b.ToTable("supplier_return_credits");
        b.HasKey(x=>x.Id);
        b.HasAlternateKey(x=>new{x.Id,x.ShopId});
        b.HasOne<Shop>().WithMany().HasForeignKey(x=>x.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x=>x.Reason).HasMaxLength(255);
        b.Property(x=>x.SupplierReference).HasMaxLength(255);
        b.Property(x=>x.BusinessTimeZoneId).HasMaxLength(80);
        b.HasIndex(x=>x.SupplierReturnId).IsUnique();
        b.HasMany(x=>x.Lines).WithOne().HasPrincipalKey(x=>new{x.Id,x.ShopId}).HasForeignKey(x=>new{x.SupplierReturnCreditId,x.ShopId}).OnDelete(DeleteBehavior.Restrict);
    }
}
