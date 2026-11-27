using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class BatchExpiryRevisionConfiguration : IEntityTypeConfiguration<BatchExpiryRevision>
{
    public void Configure(EntityTypeBuilder<BatchExpiryRevision> builder)
    {
        builder.ToTable("batch_expiry_revisions", table =>
        {
            table.HasCheckConstraint("ck_batch_expiry_revisions_dates", "new_expiry_date IS NULL OR new_manufacturing_date IS NULL OR new_manufacturing_date <= new_expiry_date");
            table.HasCheckConstraint("ck_batch_expiry_revisions_version", "catalog_version > 1 AND previous_batch_version > 0");
            table.HasCheckConstraint("ck_batch_expiry_revisions_reason", "length(btrim(reason)) BETWEEN 1 AND 500");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.ShopId, x.CatalogVersion }).IsUnique();
        builder.HasIndex(x => new { x.ShopId, x.InventoryBatchId, x.EffectiveAt });
        builder.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryBatch>().WithMany().HasPrincipalKey(x => new { x.Id, x.ItemId, x.ShopId }).HasForeignKey(x => new { x.InventoryBatchId, x.ItemId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
    }
}
