using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class TillSourceRecordConfiguration : IEntityTypeConfiguration<TillSourceRecord>
{
    public void Configure(EntityTypeBuilder<TillSourceRecord> b)
    {
        b.ToTable("till_source_records", t =>
        {
            t.HasCheckConstraint("ck_till_sources_type", "source_type BETWEEN 1 AND 12");
            t.HasCheckConstraint("ck_till_sources_labels", "length(btrim(purpose)) BETWEEN 1 AND 100 AND length(btrim(operation_key)) BETWEEN 1 AND 128");
            t.HasCheckConstraint("ck_till_sources_times", "recorded_at >= occurred_at");
            t.HasCheckConstraint("ck_till_sources_settlement", "abs(store_credit_issued) <= 9999999999999999.99 AND store_credit_issued = round(store_credit_issued,2) AND abs(store_credit_redeemed) <= 9999999999999999.99 AND store_credit_redeemed = round(store_credit_redeemed,2) AND abs(receivable_change) <= 9999999999999999.99 AND receivable_change = round(receivable_change,2)");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.Id, x.SessionId, x.ShopId });
        b.HasIndex(x => new { x.ShopId, x.SourceType, x.SourceId, x.Purpose }).IsUnique();
        b.Property(x => x.Purpose).HasMaxLength(100);
        b.Property(x => x.OperationKey).HasMaxLength(128);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.OverrideReason).HasMaxLength(500);
        b.OwnsOne(x => x.Settlement, s =>
        {
            s.Property(x => x.StoreCreditIssued).HasColumnName("store_credit_issued").HasColumnType("numeric");
            s.Property(x => x.StoreCreditRedeemed).HasColumnName("store_credit_redeemed").HasColumnType("numeric");
            s.Property(x => x.ReceivableChange).HasColumnName("receivable_change").HasColumnType("numeric");
        });
        b.Navigation(x => x.Settlement).IsRequired();
        b.HasOne<TillSession>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { Id = x.SessionId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShopMembership>().WithMany().HasPrincipalKey(x => new { x.ShopId, x.UserId })
            .HasForeignKey(x => new { x.ShopId, UserId = x.ActorUserId }).OnDelete(DeleteBehavior.Restrict);
    }
}
