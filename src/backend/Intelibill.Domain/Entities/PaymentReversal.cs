using Intelibill.Domain.ValueObjects;
using ErrorOr;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

/// <summary>
/// The one reversal of a <see cref="PaymentReceipt"/>: the whole amount is owed again, every
/// allocation of the receipt stops counting, and the original receipt stays unchanged.
/// </summary>
public sealed class PaymentReversal : BaseEntity
{
    public const int MinReasonLength = 3;
    public const int MaxReasonLength = 255;

    public TillSettlementEvidence? SettlementEvidence { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid ReceiptId { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;

    /// <summary>The ledger entry that puts the amount back on the account.</summary>
    public Guid LedgerEntryId { get; private set; }

    public DateOnly BusinessDate { get; private set; }
    public Guid ReversedBy { get; private set; }

    private PaymentReversal() { }

    public static ErrorOr<PaymentReversal> Create(
        PaymentReceipt receipt,
        string? reason,
        Guid ledgerEntryId,
        DateOnly businessDate,
        Guid reversedBy,
        TillSettlementEvidence? settlementEvidence = null)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length is < MinReasonLength or > MaxReasonLength)
            return Error.Validation("Payment.ReversalReasonInvalid", $"A reversal reason of {MinReasonLength} to {MaxReasonLength} characters is required.");

        return new PaymentReversal
        {
            ShopId = receipt.ShopId,
            ReceiptId = receipt.Id,
            Amount = receipt.Amount,
            Reason = trimmed,
            LedgerEntryId = ledgerEntryId,
            BusinessDate = businessDate,
            ReversedBy = reversedBy,
            SettlementEvidence = settlementEvidence,
        };
    }
}
