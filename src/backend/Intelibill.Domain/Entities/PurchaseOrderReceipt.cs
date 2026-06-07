using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class PurchaseOrderReceipt : BaseEntity
{
    private readonly List<PurchaseOrderReceiptLine> _lines = [];

    public Guid ShopId { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public string ReceiptNumber { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedBy { get; private set; }
    public IReadOnlyList<PurchaseOrderReceiptLine> Lines => _lines.AsReadOnly();

    private PurchaseOrderReceipt() { }

    public static PurchaseOrderReceipt Create(
        Guid shopId,
        Guid purchaseOrderId,
        string receiptNumber,
        DateTimeOffset receivedAt,
        string? referenceNumber,
        string? notes,
        Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(receiptNumber))
            throw new ArgumentException("Receipt number is required.", nameof(receiptNumber));

        return new PurchaseOrderReceipt
        {
            ShopId = shopId,
            PurchaseOrderId = purchaseOrderId,
            ReceiptNumber = receiptNumber.Trim(),
            ReceivedAt = receivedAt.ToUniversalTime(),
            ReferenceNumber = NormalizeOptional(referenceNumber),
            Notes = NormalizeOptional(notes),
            CreatedBy = createdBy,
        };
    }

    public PurchaseOrderReceiptLine AddLine(
        Guid purchaseOrderLineId,
        Guid itemId,
        Guid inventoryBatchId,
        Guid stockTransactionId,
        decimal quantity,
        decimal totalPurchaseCost,
        decimal mrp,
        decimal salesPrice,
        decimal taxRatePercent,
        bool taxIncluded,
        bool purchaseTaxIncluded)
    {
        var line = PurchaseOrderReceiptLine.Create(
            Id,
            purchaseOrderLineId,
            itemId,
            inventoryBatchId,
            stockTransactionId,
            quantity,
            totalPurchaseCost,
            mrp,
            salesPrice,
            taxRatePercent,
            taxIncluded,
            purchaseTaxIncluded);

        _lines.Add(line);
        return line;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
