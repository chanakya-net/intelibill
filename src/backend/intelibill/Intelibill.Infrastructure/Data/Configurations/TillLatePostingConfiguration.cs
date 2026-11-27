using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class TillLatePostingConfiguration : IEntityTypeConfiguration<TillLatePosting>
{
    public void Configure(EntityTypeBuilder<TillLatePosting> b)
    {
        b.ToTable("till_late_postings", t =>
        {
            t.HasCheckConstraint("ck_till_late_postings_evidence_reason", "evidence_reason IS NULL OR length(btrim(evidence_reason)) BETWEEN 1 AND 500");
            t.HasCheckConstraint("ck_till_late_postings_evidence", "NOT included_in_count AND evidence_status IN ('Unknown','Recorded') AND length(btrim(manager_reason)) BETWEEN 1 AND 500 AND recorded_at >= occurred_at AND (close_id IS NULL OR session_id IS NOT NULL) AND jsonb_typeof(tender_lines) = 'array' AND jsonb_array_length(tender_lines) <= 1 AND (evidence_status <> 'Unknown' OR jsonb_array_length(tender_lines) = 0)");
        });
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ShopId, x.OfflineSubmissionId }).IsUnique();
        b.HasIndex(x => new { x.ShopId, x.SaleId }).IsUnique();
        b.Property(x => x.EvidenceStatus).HasMaxLength(32);
        b.Property(x => x.ManagerReason).HasMaxLength(500);
        b.Property(x => x.EvidenceReason).HasMaxLength(500);
        b.Property<IReadOnlyList<TenderLine>?>(nameof(TillLatePosting.TenderLines)).IsRequired().HasColumnType("jsonb")
            .HasConversion(InvoiceDocumentJson.Converter<IReadOnlyList<TenderLine>>(), InvoiceDocumentJson.Comparer<IReadOnlyList<TenderLine>>());
        b.HasOne<TillSession>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { Id = x.SessionId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TillClose>().WithMany().HasPrincipalKey(x => new { x.Id, x.SessionId, x.ShopId })
            .HasForeignKey(x => new { Id = x.CloseId, x.SessionId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Sale>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { Id = x.SaleId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<OfflineSubmission>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { Id = x.OfflineSubmissionId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ReconciliationIssue>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { Id = x.CaseId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShopMembership>().WithMany().HasPrincipalKey(x => new { x.ShopId, x.UserId })
            .HasForeignKey(x => new { x.ShopId, UserId = x.CashierId }).OnDelete(DeleteBehavior.Restrict);
    }
}
