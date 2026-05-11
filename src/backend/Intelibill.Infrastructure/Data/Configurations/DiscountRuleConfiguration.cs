using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class DiscountRuleConfiguration : IEntityTypeConfiguration<DiscountRule>
{
    public void Configure(EntityTypeBuilder<DiscountRule> builder)
    {
        builder.ToTable("discount_rules", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_discount_rules_percentage_range", "percentage > 0 AND percentage <= 100");
            tableBuilder.HasCheckConstraint("ck_discount_rules_threshold_amount_positive", "threshold_amount IS NULL OR threshold_amount > 0");
            tableBuilder.HasCheckConstraint("ck_discount_rules_ends_after_starts", "starts_at IS NULL OR ends_at IS NULL OR ends_at > starts_at");
            tableBuilder.HasCheckConstraint(
                "ck_discount_rules_disabled_audit",
                "(is_active = true AND disabled_at IS NULL) OR (is_active = false AND disabled_at IS NOT NULL)");
            tableBuilder.HasCheckConstraint(
                "ck_discount_rules_threshold_required",
                "(rule_type != 'SaleThresholdPercentage') OR (threshold_amount IS NOT NULL)");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ShopId)
            .IsRequired();

        builder.Property(r => r.RuleType)
            .HasConversion(r => RuleTypeToProvider(r), v => RuleTypeFromProvider(v))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.Description)
            .HasMaxLength(1000);

        builder.Property(r => r.InventoryBatchId);

        builder.Property(r => r.Percentage)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(r => r.ThresholdAmount)
            .HasPrecision(18, 2);

        builder.Property(r => r.StartsAt);

        builder.Property(r => r.EndsAt);

        builder.Property(r => r.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(r => r.DisabledAt);

        builder.Property(r => r.DisabledReason)
            .HasMaxLength(500);

        builder.Property(r => r.BelowCostConfirmed)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(r => r.BelowCostConfirmationReason)
            .HasMaxLength(500);

        builder.Property(r => r.ReplacesRuleId);

        builder.Property(r => r.ReplacedByRuleId);

        builder.Property(r => r.CreatedBy)
            .IsRequired();

        builder.Property(r => r.UpdatedBy);

        builder.HasIndex(r => new { r.ShopId, r.IsActive });
        builder.HasIndex(r => new { r.ShopId, r.RuleType });
        builder.HasIndex(r => new { r.ShopId, r.InventoryBatchId })
            .HasFilter("inventory_batch_id IS NOT NULL");

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(r => r.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<InventoryBatch>()
            .WithMany()
            .HasForeignKey(r => r.InventoryBatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string RuleTypeToProvider(DiscountRuleType ruleType) =>
        ruleType switch
        {
            DiscountRuleType.BatchPercentage => "BatchPercentage",
            DiscountRuleType.SalePercentage => "SalePercentage",
            DiscountRuleType.SaleThresholdPercentage => "SaleThresholdPercentage",
            _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, null)
        };

    private static DiscountRuleType RuleTypeFromProvider(string value) =>
        value switch
        {
            "BatchPercentage" => DiscountRuleType.BatchPercentage,
            "SalePercentage" => DiscountRuleType.SalePercentage,
            "SaleThresholdPercentage" => DiscountRuleType.SaleThresholdPercentage,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
}
