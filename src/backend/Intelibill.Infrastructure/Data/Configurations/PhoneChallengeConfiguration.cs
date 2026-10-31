using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class PhoneChallengeConfiguration : IEntityTypeConfiguration<PhoneChallenge>
{
    public void Configure(EntityTypeBuilder<PhoneChallenge> builder)
    {
        builder.ToTable("phone_challenges");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Purpose).HasMaxLength(32);
        builder.Property(x => x.Destination).HasMaxLength(16);
        builder.Property(x => x.CodeHash).HasMaxLength(64);
        builder.Property(x => x.KeyId).HasMaxLength(32);
        builder.HasIndex(x => new { x.Destination, x.CreatedAt });
        builder.HasIndex(x => new { x.UserId, x.Purpose });
    }
}

internal sealed class IdentitySmsConfiguration : IEntityTypeConfiguration<IdentitySms>
{
    public void Configure(EntityTypeBuilder<IdentitySms> builder)
    {
        builder.ToTable("identity_sms");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Destination).HasMaxLength(16);
        builder.HasIndex(x => new { x.Destination, x.CreatedAt });
        builder.HasIndex(x => new { x.CompletedAt, x.NextAttemptAt });
        builder.HasIndex(x => x.ChallengeId);
    }
}
