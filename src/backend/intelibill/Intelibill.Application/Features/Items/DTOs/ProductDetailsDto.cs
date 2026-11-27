namespace Intelibill.Application.Features.Items.DTOs;

/// <param name="PurchaseTaxIncluded">Whether <c>CostPrice</c> includes tax; null when there is no previous batch.</param>
public sealed record ProductDetailsDto(
    string Name,
    string Description,
    string Uom,
    decimal CostPrice,
    decimal Mrp,
    decimal SalesPrice,
    Guid? SupplierId,
    string? SupplierName,
    bool? TaxIncluded,
    decimal? TaxRatePercent,
    string? HsnCode,
    bool? PurchaseTaxIncluded = null);
