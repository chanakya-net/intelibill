using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ShopId)
            .IsRequired();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(180);

        builder.Property(c => c.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(c => c.Address)
            .HasMaxLength(320);

        builder.Property(c => c.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(c => c.Gstin)
            .HasMaxLength(Gstin.Length);

        builder.Property(c => c.StateCode)
            .HasMaxLength(2);

        builder.Property(c => c.CreditLimit)
            .IsRequired()
            .HasColumnType("numeric(10,2)")
            .HasDefaultValue(0m);

        // Every customer is created with an explicit policy. The baseline schema still carries the
        // removed mode 3 as its column default and in its CHECK list; neither is ever written, and
        // both go with the next migration (default dropped, CHECK narrowed to (1, 2)).
        builder.Property(c => c.CreditPolicyMode)
            .IsRequired()
            .HasDefaultValue((CreditPolicyMode)3)
            .HasSentinel((CreditPolicyMode)0);

        builder.Property(c => c.CreditTermsDays);

        builder.ToTable("customers", t =>
        {
            t.HasCheckConstraint("ck_customers_credit_policy_mode", "credit_policy_mode IN (1, 2, 3)");
            t.HasCheckConstraint("ck_customers_credit_terms_days", "credit_terms_days IS NULL OR credit_terms_days BETWEEN 0 AND 365");
        });

        builder.HasIndex(c => c.ShopId);
        builder.HasIndex(c => new { c.ShopId, c.IsActive });
        builder.HasIndex(c => new { c.ShopId, c.PhoneNumber });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(c => c.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
