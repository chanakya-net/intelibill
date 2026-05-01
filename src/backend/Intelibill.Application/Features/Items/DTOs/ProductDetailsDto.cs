namespace Intelibill.Application.Features.Items.DTOs;

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
    decimal? TaxRatePercent);
