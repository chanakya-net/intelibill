using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ExpiryPolicyRevisionConfiguration : IEntityTypeConfiguration<ExpiryPolicyRevision>
{
    public void Configure(EntityTypeBuilder<ExpiryPolicyRevision> builder)
    {
        builder.ToTable("expiry_policy_revisions", t =>
        {
            t.HasCheckConstraint("ck_expiry_policy_revisions_settings", "catalog_version >= 1 AND ((item_id IS NOT NULL AND mode IS NULL AND expiry_required IS NULL AND sell_through_expiry_date IS NULL AND warning_days IS NULL AND override_allowed IS NULL) OR (mode IS NOT NULL AND mode BETWEEN 0 AND 2 AND expiry_required IS NOT NULL AND sell_through_expiry_date IS NOT NULL AND warning_days IS NOT NULL AND warning_days BETWEEN 0 AND 365 AND override_allowed IS NOT NULL))");
            t.HasCheckConstraint("ck_expiry_policy_revisions_reason", "length(btrim(reason)) BETWEEN 1 AND 500");
            t.HasCheckConstraint("ck_expiry_policy_revisions_baseline", "(NOT pre_feature_baseline AND actor_user_id IS NOT NULL) OR (pre_feature_baseline AND catalog_version = 1 AND item_id IS NULL AND mode = 0 AND expiry_required = false AND sell_through_expiry_date = true AND warning_days = 7 AND override_allowed = false AND actor_user_id IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.Policy);
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.ShopId, x.CatalogVersion }).IsUnique();
        builder.HasIndex(x => new { x.ShopId, x.ItemId, x.EffectiveAt });
        builder.HasOne<ShopExpiryState>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Item>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId }).HasForeignKey(x => new { x.ItemId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
    }
}
