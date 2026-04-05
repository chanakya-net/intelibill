namespace Intelibill.Application.Features.Items.DTOs;

public sealed record ProductDetailsDto(
    string Description,
    string Uom,
    decimal CostPrice,
    decimal Mrp,
    decimal SalesPrice,
    decimal MinSalePrice);
