using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class EmailOwnershipChallengeConfiguration : IEntityTypeConfiguration<EmailOwnershipChallenge>
{
    public void Configure(EntityTypeBuilder<EmailOwnershipChallenge> builder)
    {
        builder.ToTable("email_ownership_challenges");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Purpose).HasMaxLength(32);
        builder.Property(x => x.Destination).HasMaxLength(256);
        builder.Property(x => x.TokenHash).HasMaxLength(64);
        builder.HasIndex(x => new { x.Destination, x.CreatedAt });
        builder.HasIndex(x => new { x.UserId, x.Purpose });
    }
}
internal sealed class IdentityMailConfiguration : IEntityTypeConfiguration<IdentityMail>
{
    public void Configure(EntityTypeBuilder<IdentityMail> builder)
    {
        builder.ToTable("identity_mail");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Destination).HasMaxLength(256);
        builder.HasIndex(x => new { x.Destination, x.CreatedAt });
        builder.HasIndex(x => new { x.CompletedAt, x.NextAttemptAt });
        builder.HasIndex(x => x.InvitationId);
    }
}
