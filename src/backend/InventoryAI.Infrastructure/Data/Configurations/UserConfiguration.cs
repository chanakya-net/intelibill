using InventoryAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryAI.Infrastructure.Data.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email)
            .HasMaxLength(256);

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("email IS NOT NULL");

        builder.Property(u => u.PhoneNumber)
            .HasMaxLength(32);

        builder.HasIndex(u => u.PhoneNumber)
            .IsUnique()
            .HasFilter("phone_number IS NOT NULL");

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(256);

        builder.Property(u => u.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasMany(u => u.ExternalLogins)
            .WithOne(el => el.User)
            .HasForeignKey(el => el.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
