using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class CustomerLedgerEntry : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? SaleId { get; private set; }
    public CustomerLedgerEntryType EntryType { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly EntryDate { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedBy { get; private set; }

    private CustomerLedgerEntry() { }

    public static ErrorOr<CustomerLedgerEntry> Create(
        Guid shopId,
        Guid customerId,
        Guid? saleId,
        CustomerLedgerEntryType entryType,
        decimal amount,
        DateOnly entryDate,
        string? notes,
        Guid createdBy)
    {
        var validation = Validate(entryType, saleId, amount);
        if (validation.IsError)
        {
            return validation.Errors;
        }

        return new CustomerLedgerEntry
        {
            ShopId = shopId,
            CustomerId = customerId,
            SaleId = saleId,
            EntryType = entryType,
            Amount = amount,
            EntryDate = entryDate,
            Notes = NormalizeOptional(notes),
            CreatedBy = createdBy,
        };
    }

    private static ErrorOr<Success> Validate(CustomerLedgerEntryType entryType, Guid? saleId, decimal amount)
    {
        if (amount <= 0)
        {
            return Error.Validation("CustomerLedgerEntry.AmountMustBePositive", "Amount must be greater than zero.");
        }

        if (entryType == CustomerLedgerEntryType.SaleDue && saleId is null)
        {
            return Error.Validation("CustomerLedgerEntry.SaleIdRequired", "Sale id is required for sale due entries.");
        }

        if (entryType == CustomerLedgerEntryType.PaymentReceived && saleId is not null)
        {
            return Error.Validation("CustomerLedgerEntry.SaleIdNotAllowed", "Sale id is not allowed for payment entries.");
        }

        return Result.Success;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
