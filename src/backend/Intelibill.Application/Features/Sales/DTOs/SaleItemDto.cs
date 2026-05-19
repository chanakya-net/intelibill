namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SaleItemDto
{
    public SaleItemDto(
        Guid saleItemId,
        Guid itemId,
        string itemName,
        Guid inventoryBatchId,
        decimal quantity,
        decimal salesPrice,
        decimal taxRatePercent,
        bool isPriceIncludingTax,
        bool hasPriceMismatch,
        decimal returnedQuantity = 0m,
        decimal? returnableQuantity = null,
        string returnStatus = "NotReturned")
    {
        SaleItemId = saleItemId;
        ItemId = itemId;
        ItemName = itemName;
        InventoryBatchId = inventoryBatchId;
        Quantity = quantity;
        SalesPrice = salesPrice;
        TaxRatePercent = taxRatePercent;
        IsPriceIncludingTax = isPriceIncludingTax;
        HasPriceMismatch = hasPriceMismatch;
        ReturnedQuantity = returnedQuantity;
        ReturnableQuantity = returnableQuantity ?? quantity;
        ReturnStatus = returnStatus;
    }

    public Guid SaleItemId { get; init; }
    public Guid ItemId { get; init; }
    public string ItemName { get; init; }
    public Guid InventoryBatchId { get; init; }
    public decimal Quantity { get; init; }
    public decimal SalesPrice { get; init; }
    public decimal OriginalSalesPrice { get; init; }
    public decimal FinalSalesPrice { get; init; }
    public decimal PreTaxAmountBeforeDiscount { get; init; }
    public decimal ItemDiscountAmount { get; init; }
    public decimal SaleDiscountAmount { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal SavingsAmount { get; init; }
    public decimal TaxRatePercent { get; init; }
    public bool IsPriceIncludingTax { get; init; }
    public bool HasPriceMismatch { get; init; }
    public string? HsnCode { get; init; }
    public decimal ReturnedQuantity { get; init; }
    public decimal ReturnableQuantity { get; init; }
    public string ReturnStatus { get; init; }
}
