using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class ReconciliationIssue : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid? SaleId { get; private set; }
    public string ClientSaleId { get; private set; } = string.Empty;
    public string DeviceId { get; private set; } = string.Empty;
    public ReconciliationIssueType IssueType { get; private set; }
    public Guid? ItemId { get; private set; }
    public Guid? InventoryBatchId { get; private set; }
    public decimal? PrintedQuantity { get; private set; }
    public decimal? AvailableQuantity { get; private set; }
    public decimal? ConsumedQuantity { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public bool IsResolved { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    public Guid CreatedBy { get; private set; }

    private ReconciliationIssue() { }

    public static ReconciliationIssue Create(
        Guid shopId,
        Guid? saleId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        Guid createdBy,
        Guid? itemId = null,
        Guid? inventoryBatchId = null,
        decimal? printedQuantity = null,
        decimal? availableQuantity = null,
        decimal? consumedQuantity = null)
    {
        return new ReconciliationIssue
        {
            ShopId = shopId,
            SaleId = saleId,
            ClientSaleId = clientSaleId.Trim(),
            DeviceId = deviceId.Trim(),
            IssueType = issueType,
            ItemId = itemId,
            InventoryBatchId = inventoryBatchId,
            PrintedQuantity = printedQuantity,
            AvailableQuantity = availableQuantity,
            ConsumedQuantity = consumedQuantity,
            Code = code.Trim(),
            Message = message.Trim(),
            IsResolved = false,
            CreatedBy = createdBy,
        };
    }

    public void Resolve(Guid resolvedBy, DateTimeOffset resolvedAt)
    {
        IsResolved = true;
        ResolvedBy = resolvedBy;
        ResolvedAt = resolvedAt;
    }

    public void LinkSale(Guid saleId)
    {
        SaleId = saleId;
    }
}
