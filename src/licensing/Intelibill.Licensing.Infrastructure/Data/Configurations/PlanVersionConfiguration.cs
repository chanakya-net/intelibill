using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Data.Configurations;

/// <summary>Every fact is nullable (missing); the database refuses negative, unlimited or out-of-range values.</summary>
internal sealed class PlanVersionConfiguration : IEntityTypeConfiguration<PlanVersion>
{
    internal const string PolicyVersionPattern = "'^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$'";

    public void Configure(EntityTypeBuilder<PlanVersion> builder)
    {
        builder.ToTable("plan_versions", table =>
        {
            table.HasCheckConstraint("ck_plan_versions_amounts", """
                monthly_price_paise BETWEEN 0 AND 1000000000000
                AND annual_price_paise BETWEEN 0 AND 1000000000000
                AND extra_shop_monthly_price_paise BETWEEN 0 AND 1000000000000
                AND extra_shop_annual_price_paise BETWEEN 0 AND 1000000000000
                """);
            table.HasCheckConstraint("ck_plan_versions_capacities",
                "included_shops BETWEEN 1 AND 100000 AND members_per_shop BETWEEN 1 AND 100000");
            table.HasCheckConstraint("ck_plan_versions_extra_shop_offer", """
                extra_shops_offered IS TRUE
                OR (extra_shop_monthly_price_paise IS NULL AND extra_shop_annual_price_paise IS NULL)
                """);
            table.HasCheckConstraint("ck_plan_versions_currency", "currency = 'INR'");
            table.HasCheckConstraint("ck_plan_versions_status", "status IN (1, 2, 3)");
            table.HasCheckConstraint("ck_plan_versions_source", """
                source IN (0, 1, 2) AND (source_version_id IS NULL) = (source_expected_version IS NULL)
                AND (source_expected_version IS NULL OR source_expected_version > 0)
                """);
            table.HasCheckConstraint("ck_plan_versions_numbers", "version_number >= 1 AND revision >= 1");
            table.HasCheckConstraint("ck_plan_versions_name", "name = btrim(name) AND char_length(name) BETWEEN 1 AND 80");
            table.HasCheckConstraint("ck_plan_versions_policy_versions",
                $"customer_terms_version ~ {PolicyVersionPattern} AND refund_policy_version ~ {PolicyVersionPattern}");
            table.HasCheckConstraint("ck_plan_versions_reason",
                "negotiation_reason = btrim(negotiation_reason) AND char_length(negotiation_reason) BETWEEN 1 AND 500");
        });
        builder.HasKey(version => version.Id);
        builder.Property(version => version.Id).ValueGeneratedNever();
        builder.Property(version => version.Name).HasMaxLength(80);
        builder.Property(version => version.CurrencyCode).HasColumnName("currency").HasMaxLength(3);
        builder.Property(version => version.CustomerTermsVersion).HasMaxLength(40);
        builder.Property(version => version.RefundPolicyVersion).HasMaxLength(40);
        builder.Property(version => version.NegotiationReason).HasMaxLength(500);
        builder.Property(version => version.Source).HasDefaultValue(CatalogueSource.Unknown);
        builder.Ignore(version => version.Facts);
        builder.HasOne<Plan>().WithMany().HasForeignKey(version => version.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(version => new { version.PlanId, version.VersionNumber }).IsUnique();
        builder.HasIndex(version => version.PlanId, "PublishedPlanPerPlan").IsUnique().HasFilter("status = 2")
            .HasDatabaseName("ux_plan_versions_published");
        // One open draft per plan.
        builder.HasIndex(version => version.PlanId).IsUnique().HasFilter("status = 1")
            .HasDatabaseName("ux_plan_versions_open_draft");
    }
}
