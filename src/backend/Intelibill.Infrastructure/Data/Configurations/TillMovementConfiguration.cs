using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class TillMovementConfiguration : IEntityTypeConfiguration<TillMovement>
{
    public void Configure(EntityTypeBuilder<TillMovement> b)
    {
        b.ToTable("till_movements", t =>
        {
            t.HasCheckConstraint("ck_till_movements_amount", "signed_amount <> 0 AND abs(signed_amount) <= 9999999999999999.99 AND signed_amount = round(signed_amount,2)");
            t.HasCheckConstraint("ck_till_movements_type", "method BETWEEN 2 AND 6 AND source_type BETWEEN 1 AND 12 AND sequence > 0 AND line_number = 1");
            t.HasCheckConstraint("ck_till_movements_cash", "(cash_tendered IS NULL AND change IS NULL) OR (method = 2 AND cash_tendered IS NOT NULL AND change IS NOT NULL AND change >= 0 AND cash_tendered >= abs(signed_amount) AND cash_tendered-change = abs(signed_amount) AND cash_tendered <= 9999999999999999.99 AND cash_tendered = round(cash_tendered,2) AND change = round(change,2))");
            t.HasCheckConstraint("ck_till_movements_labels", "length(btrim(purpose)) BETWEEN 1 AND 100 AND length(btrim(operation_key)) BETWEEN 1 AND 128 AND recorded_at >= occurred_at");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.Id, x.ShopId });
        b.HasIndex(x => new { x.ShopId, x.SourceType, x.SourceId, x.Purpose, x.LineNumber }).IsUnique();
        b.HasIndex(x => new { x.ShopId, x.SessionId, x.Sequence }).IsUnique();
        b.Property(x => x.Purpose).HasMaxLength(100);
        b.Property(x => x.OperationKey).HasMaxLength(128);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.OverrideReason).HasMaxLength(500);
        b.Property(x => x.ExternalReference).HasMaxLength(255);
        b.Property(x => x.SignedAmount).HasColumnType("numeric");
        b.Property(x => x.CashTendered).HasColumnType("numeric");
        b.Property(x => x.Change).HasColumnType("numeric");
        b.HasOne<TillSession>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { Id = x.SessionId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TillSourceRecord>().WithMany().HasPrincipalKey(x => new { x.Id, x.SessionId, x.ShopId })
            .HasForeignKey(x => new { Id = x.SourceRecordId, x.SessionId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BankAccount>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { Id = x.BankAccountId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TillMovement>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { Id = x.ReversesMovementId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShopMembership>().WithMany().HasPrincipalKey(x => new { x.ShopId, x.UserId })
            .HasForeignKey(x => new { x.ShopId, UserId = x.ActorUserId }).OnDelete(DeleteBehavior.Restrict);
    }
}
