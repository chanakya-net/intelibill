using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class TillCloseConfiguration : IEntityTypeConfiguration<TillClose>
{
    public void Configure(EntityTypeBuilder<TillClose> b)
    {
        b.ToTable("till_closes", t =>
        {
            t.HasCheckConstraint("ck_till_closes_amount", "opening_float >= 0 AND counted_cash >= 0 AND opening_float <= 9999999999999999.99 AND counted_cash <= 9999999999999999.99 AND opening_float = round(opening_float,2) AND counted_cash = round(counted_cash,2) AND expected_cash = round(expected_cash,2) AND variance = counted_cash - expected_cash");
            t.HasCheckConstraint("ck_till_closes_reason", "(variance = 0 AND NOT is_overdue) OR (reason IS NOT NULL AND length(btrim(reason)) BETWEEN 1 AND 500)");
            t.HasCheckConstraint("ck_till_closes_override", "closed_by = cashier_id OR (override_reason IS NOT NULL AND length(btrim(override_reason)) BETWEEN 1 AND 500)");
            t.HasCheckConstraint("ck_till_closes_sequence", "last_movement_sequence >= 0 AND (NOT awaiting_review OR variance <> 0)");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.Id, x.SessionId, x.ShopId });
        b.HasIndex(x => new { x.ShopId, x.SessionId }).IsUnique();
        b.Property(x => x.OpeningFloat).HasColumnType("numeric");
        b.Property(x => x.ExpectedCash).HasColumnType("numeric");
        b.Property(x => x.CountedCash).HasColumnType("numeric");
        b.Property(x => x.Variance).HasColumnType("numeric");
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.OverrideReason).HasMaxLength(500);
        b.Property(x => x.TillName).HasMaxLength(100);
        b.Property(x => x.CashierName).HasMaxLength(200);
        b.Property<TillTotals?>(nameof(TillClose.Totals)).IsRequired().HasColumnType("jsonb")
            .HasConversion(InvoiceDocumentJson.Converter<TillTotals>(), InvoiceDocumentJson.Comparer<TillTotals>());
        b.HasOne<TillSession>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { Id = x.SessionId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShopMembership>().WithMany().HasPrincipalKey(x => new { x.ShopId, x.UserId })
            .HasForeignKey(x => new { x.ShopId, UserId = x.ClosedBy }).OnDelete(DeleteBehavior.Restrict);
    }
}
