using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Data.Configurations;

internal sealed class BillingAccountMemberConfiguration : IEntityTypeConfiguration<BillingAccountMember>
{
    public void Configure(EntityTypeBuilder<BillingAccountMember> builder)
    {
        builder.ToTable("billing_account_members", table =>
        {
            table.HasCheckConstraint("ck_billing_account_members_ids", """
                id <> '00000000-0000-0000-0000-000000000000'
                AND merchant_user_id <> '00000000-0000-0000-0000-000000000000'
                """);
            table.HasCheckConstraint("ck_billing_account_members_role", "role IN (1, 2)");
            table.HasCheckConstraint("ck_billing_account_members_status", "status IN (1, 2)");
            table.HasCheckConstraint("ck_billing_account_members_revision", "revision >= 1");
        });
        builder.HasKey(member => member.Id);
        builder.Property(member => member.Id).ValueGeneratedNever();
        builder.HasOne<BillingAccount>().WithMany().HasForeignKey(member => member.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        // Exactly one active owner per account; a merchant user holds at most one active membership in it.
        builder.HasIndex(member => member.AccountId).IsUnique()
            .HasFilter("role = 1 AND status = 1").HasDatabaseName("ux_billing_account_members_active_owner");
        builder.HasIndex(member => new { member.AccountId, member.MerchantUserId }).IsUnique()
            .HasFilter("status = 1").HasDatabaseName("ux_billing_account_members_active_member");
        builder.HasIndex(member => new { member.MerchantUserId, member.Status, member.CreatedAt })
            .HasDatabaseName("ix_billing_account_members_actor");
    }
}
