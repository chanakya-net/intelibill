using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class StockTransaction : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid InventoryBatchId { get; private set; }
    public StockTransactionType TransactionType { get; private set; }
    public decimal Quantity { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset PerformedAt { get; private set; }
    public Guid PerformedBy { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public Item Item { get; private set; } = null!;
    public InventoryBatch InventoryBatch { get; private set; } = null!;

    private StockTransaction() { }

    public static ErrorOr<StockTransaction> Create(
        Guid shopId,
        Guid itemId,
        Guid inventoryBatchId,
        StockTransactionType transactionType,
        decimal quantity,
        string? referenceNumber,
        string? notes,
        DateTimeOffset performedAt,
        Guid performedBy,
        Guid createdBy)
    {
        var quantityValidation = ValidateQuantity(transactionType, quantity);
        if (quantityValidation.IsError)
        {
            return quantityValidation.Errors;
        }

        return new StockTransaction
        {
            ShopId = shopId,
            ItemId = itemId,
            InventoryBatchId = inventoryBatchId,
            TransactionType = transactionType,
            Quantity = quantity,
            ReferenceNumber = NormalizeOptional(referenceNumber),
            Notes = NormalizeOptional(notes),
            PerformedAt = performedAt,
            PerformedBy = performedBy,
            CreatedBy = createdBy,
        };
    }

    public void UpdateNotes(string? notes, Guid updatedBy)
    {
        Notes = NormalizeOptional(notes);
        UpdatedBy = updatedBy;
    }

    public void MarkUpdatedBy(Guid updatedBy)
    {
        UpdatedBy = updatedBy;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    private static ErrorOr<Success> ValidateQuantity(StockTransactionType transactionType, decimal quantity)
    {
        if (quantity == 0)
        {
            return Error.Validation("StockTransaction.QuantityZero", "Quantity cannot be zero.");
        }

        var requiresPositive = transactionType is StockTransactionType.In or StockTransactionType.Ret;
        var requiresNegative = transactionType is StockTransactionType.Out or StockTransactionType.Rej or StockTransactionType.Dmg or StockTransactionType.Stol;

        if (requiresPositive && quantity < 0)
        {
            return Error.Validation("StockTransaction.QuantitySignInvalid", "Quantity must be positive for IN and RET transactions.");
        }

        if (requiresNegative && quantity > 0)
        {
            return Error.Validation("StockTransaction.QuantitySignInvalid", "Quantity must be negative for OUT, REJ, DMG, and STOL transactions.");
        }

        return Result.Success;
    }
}
