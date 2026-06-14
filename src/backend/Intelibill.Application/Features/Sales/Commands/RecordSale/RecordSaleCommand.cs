using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public sealed partial record RecordSaleCommand(
    Guid ActorUserId,
    Guid ShopId,
    string IdempotencyKey,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    PaymentMethod PaymentMethod,
    decimal PaidAmount,
    decimal DueAmount,
    IReadOnlyList<RecordSaleItemCommand> Items,
    InstantDiscount? SaleDiscount = null,
    decimal CreditNoteAppliedAmount = 0m,
    IReadOnlyList<CreditNoteRedemptionCommand>? CreditNoteRedemptions = null,
    bool CreditNoteCustomerMismatchConfirmed = false);

public sealed record RecordSaleItemCommand(
    string Barcode,
    string BatchNumber,
    string ItemName,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    Guid InventoryBatchId,
    InstantDiscount? ItemDiscount = null,
    string? ClientLineKey = null,
    string? HsnCode = null,
    SaleLineType LineType = SaleLineType.Goods,
    Guid? ServiceId = null)
{
    public bool IsGoodsLine => LineType == SaleLineType.Goods;
    public bool IsServiceLine => LineType == SaleLineType.Service;
}

public record CreditNoteRedemptionCommand(
    string Code,
    decimal Amount);

public sealed record RecordSaleCreditNoteRedemptionCommand(
    string Code,
    decimal Amount) : CreditNoteRedemptionCommand(Code, Amount);
