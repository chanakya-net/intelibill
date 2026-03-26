using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class UserExternalLoginConfiguration : IEntityTypeConfiguration<UserExternalLogin>
{
    public void Configure(EntityTypeBuilder<UserExternalLogin> builder)
    {
        builder.ToTable("user_external_logins");

        builder.HasKey(el => el.Id);

        builder.Property(el => el.Provider)
            .IsRequired();

        builder.Property(el => el.ProviderKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(el => el.ProviderEmail)
            .HasMaxLength(256);

        // A given provider account can only be linked once.
        builder.HasIndex(el => new { el.Provider, el.ProviderKey })
            .IsUnique();
    }
}
