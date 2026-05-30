using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("expenses", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_expenses_amount_positive", "amount > 0");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ShopId)
            .IsRequired();

        builder.Property(e => e.CategoryId)
            .IsRequired();

        builder.Property(e => e.Amount)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(e => e.PaidTo)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.ExpenseDate)
            .IsRequired();

        builder.Property(e => e.ActorUserId)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.OriginalExpenseId);

        builder.Property(e => e.SupplierLedgerEntryId)
            .IsRequired(false);

        builder.Property(e => e.IsVoided)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasIndex(e => new { e.ShopId, e.ExpenseDate });
        builder.HasIndex(e => new { e.ShopId, e.IsVoided });
        builder.HasIndex(e => new { e.ShopId, e.PaidTo });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(e => e.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Category)
            .WithMany()
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Expense>()
            .WithMany()
            .HasForeignKey(e => e.OriginalExpenseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SupplierLedgerEntry>()
            .WithMany()
            .HasForeignKey(e => e.SupplierLedgerEntryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
