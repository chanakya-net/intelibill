using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class StockTransactionConfiguration : IEntityTypeConfiguration<StockTransaction>
{
    public void Configure(EntityTypeBuilder<StockTransaction> builder)
    {
        builder.ToTable("stock_transactions", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_stock_transactions_quantity_non_zero", "quantity <> 0");
            tableBuilder.HasCheckConstraint(
                "ck_stock_transactions_quantity_sign_by_type",
                "((transaction_type IN ('IN', 'RET') AND quantity > 0) OR (transaction_type IN ('OUT', 'REJ', 'DMG', 'STOL') AND quantity < 0) OR (transaction_type = 'ADJ'))");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.ShopId)
            .IsRequired();

        builder.Property(t => t.ItemId)
            .IsRequired();

        builder.Property(t => t.InventoryBatchId)
            .IsRequired();

        builder.Property(t => t.TransactionType)
            .HasConversion(t => ToProviderValue(t), value => FromProviderValue(value))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(t => t.Quantity)
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(t => t.ReferenceNumber)
            .HasMaxLength(120);

        builder.Property(t => t.Notes)
            .HasMaxLength(1000);

        builder.Property(t => t.PerformedAt)
            .IsRequired();

        builder.Property(t => t.PerformedBy)
            .IsRequired();

        builder.Property(t => t.CreatedBy)
            .IsRequired();

        builder.Property(t => t.UpdatedBy);

        builder.HasIndex(t => new { t.ShopId, t.ItemId, t.PerformedAt });
        builder.HasIndex(t => new { t.ShopId, t.InventoryBatchId, t.PerformedAt });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(t => t.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Item)
            .WithMany(i => i.StockTransactions)
            .HasPrincipalKey(i => new { i.Id, i.ShopId })
            .HasForeignKey(t => new { t.ItemId, t.ShopId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.InventoryBatch)
            .WithMany(b => b.StockTransactions)
            .HasPrincipalKey(b => new { b.Id, b.ItemId, b.ShopId })
            .HasForeignKey(t => new { t.InventoryBatchId, t.ItemId, t.ShopId })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToProviderValue(StockTransactionType transactionType)
    {
        return transactionType switch
        {
            StockTransactionType.In => "IN",
            StockTransactionType.Out => "OUT",
            StockTransactionType.Adj => "ADJ",
            StockTransactionType.Ret => "RET",
            StockTransactionType.Rej => "REJ",
            StockTransactionType.Dmg => "DMG",
            StockTransactionType.Stol => "STOL",
            _ => throw new InvalidOperationException($"Unsupported transaction type {transactionType}.")
        };
    }

    private static StockTransactionType FromProviderValue(string value)
    {
        return value switch
        {
            "IN" => StockTransactionType.In,
            "OUT" => StockTransactionType.Out,
            "ADJ" => StockTransactionType.Adj,
            "RET" => StockTransactionType.Ret,
            "REJ" => StockTransactionType.Rej,
            "DMG" => StockTransactionType.Dmg,
            "STOL" => StockTransactionType.Stol,
            _ => throw new InvalidOperationException($"Unsupported transaction type code {value}.")
        };
    }
}