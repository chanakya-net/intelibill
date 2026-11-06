using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

/// <summary>
/// One posted payment: money collected from a customer or paid to a supplier, with how it moved
/// and the ledger entry that carries it into the account balance.
/// </summary>
/// <remarks>
/// <para>
/// Immutable once posted. Which invoices or purchase receipts it settled lives in
/// <see cref="PaymentAllocation"/> rows; a mistake is corrected by one <see cref="PaymentReversal"/>,
/// never by editing or deleting the receipt.
/// </para>
/// <para>
/// A <see cref="IsLegacy"/> receipt was backfilled from a ledger payment recorded before receipts
/// existed. Its method is <see cref="ReceiptTenderMethod.UnknownRecordedMethod"/> and it has no
/// allocation: neither is inferred from history.
/// </para>
/// </remarks>
public sealed class PaymentReceipt : BaseEntity
{
    public const int MaxExternalReferenceLength = 80;
    public const int MaxNotesLength = 500;

    public Guid ShopId { get; private set; }
    public PaymentPartyType PartyType { get; private set; }
    public Guid PartyId { get; private set; }
    public decimal Amount { get; private set; }
    public ReceiptTenderMethod Method { get; private set; }
    public Guid? BankAccountId { get; private set; }
    public string? ExternalReference { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>The customer (<c>PAYMENT_RECEIVED</c>) or supplier (<c>PAYMENT_MADE</c>) ledger entry.</summary>
    public Guid LedgerEntryId { get; private set; }

    /// <summary>The expense a supplier payment also recorded, if any.</summary>
    public Guid? ExpenseId { get; private set; }

    public bool IsLegacy { get; private set; }
    public Guid CreatedBy { get; private set; }

    private PaymentReceipt() { }

    public static ErrorOr<PaymentReceipt> Record(
        Guid shopId,
        PaymentPartyType partyType,
        Guid partyId,
        decimal amount,
        ReceiptTenderMethod method,
        Guid? bankAccountId,
        string? externalReference,
        DateOnly businessDate,
        DateTimeOffset receivedAt,
        string? notes,
        Guid ledgerEntryId,
        Guid? expenseId,
        Guid createdBy)
    {
        if (amount <= 0m || decimal.Round(amount, 2) != amount)
            return Error.Validation("Payment.AmountInvalid", "Payment amount must be greater than zero with at most two decimals.");

        if (!Enum.IsDefined(partyType))
            return Error.Validation("Payment.PartyTypeInvalid", "Payment party type is not supported.");

        if (!Enum.IsDefined(method))
            return Error.Validation("Payment.MethodInvalid", "Payment method is not supported.");

        var reference = Normalize(externalReference);
        if (reference is { Length: > MaxExternalReferenceLength })
            return Error.Validation("Payment.ReferenceTooLong", $"Payment reference must not exceed {MaxExternalReferenceLength} characters.");

        var trimmedNotes = Normalize(notes);
        if (trimmedNotes is { Length: > MaxNotesLength })
            return Error.Validation("Payment.NotesTooLong", $"Payment notes must not exceed {MaxNotesLength} characters.");

        if (expenseId.HasValue && partyType != PaymentPartyType.Supplier)
            return Error.Validation("Payment.ExpenseNotAllowed", "Only a supplier payment can carry an expense.");

        return new PaymentReceipt
        {
            ShopId = shopId,
            PartyType = partyType,
            PartyId = partyId,
            Amount = amount,
            Method = method,
            BankAccountId = bankAccountId,
            ExternalReference = reference,
            BusinessDate = businessDate,
            ReceivedAt = receivedAt.ToUniversalTime(),
            Notes = trimmedNotes,
            LedgerEntryId = ledgerEntryId,
            ExpenseId = expenseId,
            IsLegacy = false,
            CreatedBy = createdBy,
        };
    }

    /// <summary>The allocation target type this receipt's party can settle.</summary>
    public PaymentAllocationTargetType AllocationTargetType =>
        PartyType == PaymentPartyType.Customer
            ? PaymentAllocationTargetType.CustomerInvoice
            : PaymentAllocationTargetType.SupplierReceipt;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
