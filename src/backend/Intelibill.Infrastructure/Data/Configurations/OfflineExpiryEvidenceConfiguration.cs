using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Intelibill.Infrastructure.Data.Configurations;
internal sealed class OfflineExpiryEvidenceConfiguration : IEntityTypeConfiguration<OfflineExpiryEvidence>
{
    public void Configure(EntityTypeBuilder<OfflineExpiryEvidence> builder)
    {
        builder.ToTable("offline_expiry_evidence", t =>
        {
            t.HasCheckConstraint("ck_offline_expiry_evidence_version", "catalog_version > 0");
            t.HasCheckConstraint("ck_offline_expiry_evidence_window", "expires_at = captured_at + interval '7 days'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EffectivePolicy).HasColumnType("jsonb").HasConversion(x => JsonSerializer.Serialize(x, (JsonSerializerOptions?)null), x => JsonSerializer.Deserialize<ExpiryPolicy>(x, (JsonSerializerOptions?)null)!);
        builder.Property(x => x.BatchFacts).HasColumnType("jsonb").HasConversion(x => JsonSerializer.Serialize(x, (JsonSerializerOptions?)null), x => JsonSerializer.Deserialize<ExpiryBatchFacts>(x, (JsonSerializerOptions?)null)!);
        builder.HasIndex(x => new { x.ShopId, x.SnapshotId, x.BatchId }).IsUnique();
        builder.HasIndex(x => new { x.ShopId, x.BatchId, x.CapturedAt });
        builder.HasOne<InventoryBatch>().WithMany().HasPrincipalKey(x => new { x.Id, x.ItemId, x.ShopId }).HasForeignKey(x => new { x.BatchId, x.ItemId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Restrict);
    }
}
