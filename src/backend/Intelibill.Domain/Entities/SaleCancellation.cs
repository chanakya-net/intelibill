using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

/// <summary>Immutable commercial cancellation facts; the issued sale remains intact.</summary>
public sealed class SaleCancellation : BaseEntity
{
    private readonly List<SaleCancellationLine> _lines = [];
    public Guid ShopId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string OperationKey { get; private set; } = string.Empty;
    public string ReferenceNumber { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset CancelledAt { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string BusinessTimeZoneId { get; private set; } = string.Empty;
    public string PolicyVersion { get; private set; } = string.Empty;
    public string OriginalInvoiceNumber { get; private set; } = string.Empty;
    public DateOnly OriginalBusinessDate { get; private set; }
    public decimal TotalAmount { get; private set; }
    public decimal RevenueAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal DueReductionAmount { get; private set; }
    public decimal CashRefundAmount { get; private set; }
    public Guid? CustomerLedgerEntryId { get; private set; }
    public InvoiceSeller Seller { get; private set; } = null!;
    public InvoiceBuyer Buyer { get; private set; } = null!;
    public bool GoodsRecoveredConfirmed { get; private set; }
    public bool CashRefundConfirmed { get; private set; }
    public IReadOnlyList<SaleCancellationLine> Lines => _lines.AsReadOnly();

    private SaleCancellation() { }

    public static ErrorOr<SaleCancellation> Create(
        Guid shopId, Guid saleId, Guid actorUserId, string operationKey, string reason,
        DateTimeOffset cancelledAt, DateOnly businessDate, string businessTimeZoneId,
        string originalInvoiceNumber, DateOnly originalBusinessDate,
        decimal totalAmount, decimal revenueAmount, decimal taxAmount, decimal dueReductionAmount,
        decimal cashRefundAmount, Guid? customerLedgerEntryId, InvoiceSeller seller, InvoiceBuyer buyer,
        bool goodsRecoveredConfirmed, bool cashRefundConfirmed)
    {
        if (shopId == Guid.Empty || saleId == Guid.Empty || actorUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(operationKey) || operationKey.Trim().Length > 128 ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 255 ||
            string.IsNullOrWhiteSpace(businessTimeZoneId) || string.IsNullOrWhiteSpace(originalInvoiceNumber) ||
            seller is null || buyer is null)
            return Error.Validation("SaleCancellation.FactsInvalid", "Cancellation identity, reason and frozen invoice facts are required.");

        if (totalAmount < 0 || revenueAmount < 0 || dueReductionAmount < 0 || cashRefundAmount < 0 ||
            taxAmount != 0 || revenueAmount != totalAmount || totalAmount != dueReductionAmount + cashRefundAmount ||
            (dueReductionAmount > 0) != (customerLedgerEntryId.HasValue && customerLedgerEntryId != Guid.Empty) ||
            (dueReductionAmount == 0 && customerLedgerEntryId.HasValue) ||
            cashRefundConfirmed != (cashRefundAmount > 0) || !goodsRecoveredConfirmed)
            return Error.Validation("SaleCancellation.SettlementInvalid", "Cancellation must fully settle the original total with linked due credit and confirmed cash refund.");

        return new SaleCancellation
        {
            ShopId = shopId, SaleId = saleId, ActorUserId = actorUserId,
            OperationKey = operationKey.Trim(), ReferenceNumber = $"SC-{Guid.NewGuid():N}", Reason = reason.Trim(),
            CancelledAt = cancelledAt.ToUniversalTime(), BusinessDate = businessDate, BusinessTimeZoneId = businessTimeZoneId,
            PolicyVersion = "receipt-cash-due/1", OriginalInvoiceNumber = originalInvoiceNumber, OriginalBusinessDate = originalBusinessDate,
            TotalAmount = totalAmount, RevenueAmount = revenueAmount, TaxAmount = taxAmount,
            DueReductionAmount = dueReductionAmount, CashRefundAmount = cashRefundAmount, CustomerLedgerEntryId = customerLedgerEntryId,
            Seller = seller, Buyer = buyer, GoodsRecoveredConfirmed = goodsRecoveredConfirmed, CashRefundConfirmed = cashRefundConfirmed,
        };
    }

    public ErrorOr<Success> AddLine(SaleCancellationLine line)
    {
        if (line.ShopId != ShopId || line.SaleId != SaleId || line.SaleCancellationId != Id || _lines.Any(x => x.SaleItemId == line.SaleItemId))
            return Error.Validation("SaleCancellation.LineInvalid", "Each original sale item must appear once under its own cancellation and shop.");

        _lines.Add(line);
        return Result.Success;
    }
}
