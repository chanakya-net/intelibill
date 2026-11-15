using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Data.Configurations;

internal sealed class BillingAccountConfiguration : IEntityTypeConfiguration<BillingAccount>
{
    public void Configure(EntityTypeBuilder<BillingAccount> builder)
    {
        builder.ToTable("billing_accounts", table =>
        {
            table.HasCheckConstraint("ck_billing_accounts_ids", """
                id <> '00000000-0000-0000-0000-000000000000'
                AND created_by_merchant_user_id <> '00000000-0000-0000-0000-000000000000'
                AND creation_key <> '00000000-0000-0000-0000-000000000000'
                """);
            table.HasCheckConstraint("ck_billing_accounts_name",
                "name = btrim(name) AND char_length(name) BETWEEN 1 AND 120");
            table.HasCheckConstraint("ck_billing_accounts_creation_request_hash", "octet_length(creation_request_hash) = 32");
        });
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).ValueGeneratedNever();
        builder.Property(account => account.Name).HasMaxLength(120);
        builder.Property(account => account.CreationRequestHash).HasColumnType("bytea");
        // The creation fence: one account per (creating actor, operation key).
        builder.HasIndex(account => new { account.CreatedByMerchantUserId, account.CreationKey }).IsUnique();
    }
}
