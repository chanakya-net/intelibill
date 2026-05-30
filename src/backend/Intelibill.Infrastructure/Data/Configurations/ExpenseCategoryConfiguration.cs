using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("expense_categories");

        builder.HasKey(ec => ec.Id);

        builder.Property(ec => ec.ShopId)
            .IsRequired();

        builder.Property(ec => ec.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(ec => new { ec.ShopId, ec.Name })
            .IsUnique();

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(ec => ec.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
