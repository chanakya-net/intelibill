using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class TillSessionConfiguration : IEntityTypeConfiguration<TillSession>
{
    public void Configure(EntityTypeBuilder<TillSession> b)
    {
        b.ToTable("till_sessions", t =>
        {
            t.HasCheckConstraint("ck_till_sessions_state", "state IN (1,2)");
            t.HasCheckConstraint("ck_till_sessions_amount", "opening_float >= 0 AND opening_float <= 9999999999999999.99 AND opening_float = round(opening_float,2)");
            t.HasCheckConstraint("ck_till_sessions_sequence", "last_movement_sequence >= 0 AND source_count >= 0");
            t.HasCheckConstraint("ck_till_sessions_labels", "length(btrim(till_name)) BETWEEN 1 AND 100 AND length(btrim(cashier_name)) BETWEEN 1 AND 200 AND length(btrim(business_time_zone_id)) BETWEEN 1 AND 64");
            t.HasCheckConstraint("ck_till_sessions_override", "opened_by = cashier_id OR (override_reason IS NOT NULL AND length(btrim(override_reason)) BETWEEN 1 AND 500)");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.Id, x.ShopId });
        b.Property(x => x.TillName).HasMaxLength(100);
        b.Property(x => x.CashierName).HasMaxLength(200);
        b.Property(x => x.BusinessTimeZoneId).HasMaxLength(64);
        b.Property(x => x.OverrideReason).HasMaxLength(500);
        b.Property(x => x.OpeningFloat).HasColumnType("numeric");
        b.HasIndex(x => new { x.ShopId, x.TillId }).IsUnique().HasFilter("state = 1");
        b.HasIndex(x => new { x.ShopId, x.CashierId, x.BusinessDate });
        b.HasOne<Till>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { Id = x.TillId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShopMembership>().WithMany().HasPrincipalKey(x => new { x.ShopId, x.UserId })
            .HasForeignKey(x => new { x.ShopId, UserId = x.CashierId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShopMembership>().WithMany().HasPrincipalKey(x => new { x.ShopId, x.UserId })
            .HasForeignKey(x => new { x.ShopId, UserId = x.OpenedBy }).OnDelete(DeleteBehavior.Restrict);
    }
}
