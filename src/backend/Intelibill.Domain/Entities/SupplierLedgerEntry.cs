using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class SupplierLedgerEntry : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid? BatchId { get; private set; }
    public SupplierLedgerEntryType EntryType { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly EntryDate { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedBy { get; private set; }

    private SupplierLedgerEntry() { }

    public static ErrorOr<SupplierLedgerEntry> Create(
        Guid shopId,
        Guid supplierId,
        Guid? batchId,
        SupplierLedgerEntryType entryType,
        decimal amount,
        DateOnly entryDate,
        string? notes,
        Guid createdBy)
    {
        var validation = Validate(entryType, batchId, amount);
        if (validation.IsError)
        {
            return validation.Errors;
        }

        var entry = new SupplierLedgerEntry
        {
            ShopId = shopId,
            SupplierId = supplierId,
            BatchId = batchId,
            EntryType = entryType,
            Amount = amount,
            EntryDate = entryDate,
            Notes = NormalizeOptional(notes),
            CreatedBy = createdBy,
        };

        return entry;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    private static ErrorOr<Success> Validate(SupplierLedgerEntryType entryType, Guid? batchId, decimal amount)
    {
        if (entryType == SupplierLedgerEntryType.SupplierReturnCredit && amount >= 0)
            return Error.Validation("SupplierLedgerEntry.CreditSignInvalid", "Supplier return credit must be negative.");

        if (amount == 0)
        {
            return Error.Validation("SupplierLedgerEntry.AmountZero", "Amount cannot be zero.");
        }

        if (entryType is (SupplierLedgerEntryType.GoodsReceived or SupplierLedgerEntryType.CostAdjusted or SupplierLedgerEntryType.SupplierReturnCredit) && batchId is null)
        {
            return Error.Validation("SupplierLedgerEntry.BatchRequired", "Batch is required for goods received and cost adjustment entries.");
        }

        if (entryType is (SupplierLedgerEntryType.PaymentMade or SupplierLedgerEntryType.RecordAdjusted) && batchId is not null)
        {
            return Error.Validation("SupplierLedgerEntry.BatchNotAllowed", "Batch can only be set for receipt-linked entries.");
        }

        return Result.Success;
    }

    public void ReassignSupplier(Guid supplierId)
    {
        SupplierId = supplierId;
    }
}
