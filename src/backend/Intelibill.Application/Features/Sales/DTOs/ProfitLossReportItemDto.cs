namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record ProfitLossReportItemDto(
    Guid? SaleId,
    string ReferenceNumber,
    DateTimeOffset OccurredAt,
    string? PartyName,
    decimal TotalCost,
    decimal WastageCost,
    decimal RevenueBeforeTax,
    decimal RevenueAfterTax,
    decimal ProfitBeforeTax,
    decimal ProfitAfterTax,
    decimal? MarginPercent,
    string RowType,
    Guid? InventoryAdjustmentId);

public static class ProfitLossReportRowTypes
{
    public const string Sale = nameof(Sale);
    public const string SaleReturn = nameof(SaleReturn);
    public const string InventoryAdjustment = nameof(InventoryAdjustment);
}
