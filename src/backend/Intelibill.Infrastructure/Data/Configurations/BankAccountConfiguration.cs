using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.ToTable("bank_accounts");

        builder.HasKey(ba => ba.Id);

        builder.Property(ba => ba.BankName)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(ba => ba.AccountNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(ba => ba.AccountType)
            .HasMaxLength(16)
            .HasConversion<string>();

        builder.Property(ba => ba.IfscCode)
            .HasMaxLength(20);

        builder.Property(ba => ba.AccountHolderName)
            .HasMaxLength(120);

        builder.HasOne<Shop>()
            .WithMany(s => s.BankAccounts)
            .HasForeignKey(ba => ba.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
    
    // Note: The ShopConfiguration already handles the other side of the relationship.
}
