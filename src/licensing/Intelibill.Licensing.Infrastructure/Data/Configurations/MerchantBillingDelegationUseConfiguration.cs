using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Data.Configurations;

internal sealed class MerchantBillingDelegationUseConfiguration : IEntityTypeConfiguration<MerchantBillingDelegationUse>
{
    public void Configure(EntityTypeBuilder<MerchantBillingDelegationUse> builder)
    {
        builder.ToTable("merchant_billing_delegation_uses", table =>
        {
            table.HasCheckConstraint("ck_merchant_billing_delegation_use_ids", """
                replay_id <> '00000000-0000-0000-0000-000000000000'
                AND account_id <> '00000000-0000-0000-0000-000000000000'
                AND actor_id <> '00000000-0000-0000-0000-000000000000'
                AND request_id <> '00000000-0000-0000-0000-000000000000'
                AND request_id <> replay_id
                AND key_id ~ '^[A-Za-z0-9_-]{1,128}$'
                """);
            table.HasCheckConstraint("ck_merchant_billing_delegation_use_window", """
                issued_at <= consumed_at AND consumed_at < expires_at
                AND expires_at - issued_at <= interval '60 seconds'
                """);
        });
        builder.HasKey(use => new { use.Issuer, use.ReplayId });
        builder.Property(use => use.Issuer).HasMaxLength(64);
        builder.Property(use => use.KeyId).HasMaxLength(128);
        builder.Property(use => use.Action).HasMaxLength(64);
        builder.HasIndex(use => new { use.AccountId, use.ActorId });
    }
}
