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

    public static ErrorOr<SaleCancellation> Record(SaleCancellationInput input)
    {
        if (input is null || input.ShopId == Guid.Empty || input.SaleId == Guid.Empty || input.ActorUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(input.OperationKey) || input.OperationKey.Trim().Length > 128 ||
            string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length is < 3 or > 255 ||
            string.IsNullOrWhiteSpace(input.BusinessTimeZoneId) || string.IsNullOrWhiteSpace(input.OriginalInvoiceNumber) ||
            input.Seller is null || input.Buyer is null)
            return Error.Validation("SaleCancellation.FactsInvalid", "Cancellation identity, reason and frozen invoice facts are required.");

        if (input.TotalAmount < 0 || input.RevenueAmount < 0 || input.DueReductionAmount < 0 || input.CashRefundAmount < 0 ||
            input.TaxAmount != 0 || input.RevenueAmount != input.TotalAmount || input.TotalAmount != input.DueReductionAmount + input.CashRefundAmount ||
            (input.DueReductionAmount > 0) != (input.CustomerLedgerEntryId.HasValue && input.CustomerLedgerEntryId != Guid.Empty) ||
            (input.DueReductionAmount == 0 && input.CustomerLedgerEntryId.HasValue) ||
            input.CashRefundConfirmed != (input.CashRefundAmount > 0) || !input.GoodsRecoveredConfirmed)
            return Error.Validation("SaleCancellation.SettlementInvalid", "Cancellation must fully settle the original total with linked due credit and confirmed cash refund.");

        if (input.Lines is null || input.Lines.Count == 0)
            return Error.Validation("SaleCancellation.LinesRequired", "A cancellation must record its original sale lines.");

        var cancellation = new SaleCancellation
        {
            ShopId = input.ShopId, SaleId = input.SaleId, ActorUserId = input.ActorUserId,
            OperationKey = input.OperationKey.Trim(), ReferenceNumber = $"SC-{Guid.NewGuid():N}", Reason = input.Reason.Trim(),
            CancelledAt = input.CancelledAt.ToUniversalTime(), BusinessDate = input.BusinessDate, BusinessTimeZoneId = input.BusinessTimeZoneId,
            PolicyVersion = "receipt-cash-due/1", OriginalInvoiceNumber = input.OriginalInvoiceNumber, OriginalBusinessDate = input.OriginalBusinessDate,
            TotalAmount = input.TotalAmount, RevenueAmount = input.RevenueAmount, TaxAmount = input.TaxAmount,
            DueReductionAmount = input.DueReductionAmount, CashRefundAmount = input.CashRefundAmount, CustomerLedgerEntryId = input.CustomerLedgerEntryId,
            Seller = input.Seller, Buyer = input.Buyer, GoodsRecoveredConfirmed = input.GoodsRecoveredConfirmed, CashRefundConfirmed = input.CashRefundConfirmed,
        };
        HashSet<Guid> saleItemIds = [];
        HashSet<Guid> stockTransactionIds = [];
        foreach (var lineInput in input.Lines)
        {
            if (lineInput is null || lineInput.ShopId != input.ShopId || lineInput.SaleId != input.SaleId ||
                !saleItemIds.Add(lineInput.SaleItemId) || !stockTransactionIds.Add(lineInput.StockTransactionId))
                return Error.Validation("SaleCancellation.LineInvalid", "Each original sale item and stock effect must appear once under its own sale and shop.");

            var line = SaleCancellationLine.Create(cancellation.Id, lineInput);
            if (line.IsError)
                return line.Errors;
            cancellation._lines.Add(line.Value);
        }
        if (cancellation._lines.Sum(x => x.RevenueAmount) != input.RevenueAmount)
            return Error.Validation("SaleCancellation.LinesIncomplete", "Cancellation lines must reconcile to the original revenue.");

        return cancellation;
    }
}
