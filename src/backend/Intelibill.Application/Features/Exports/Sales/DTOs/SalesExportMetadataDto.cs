namespace Intelibill.Application.Features.Exports.Sales.DTOs;

public sealed record SalesExportMetadataDto(
    string ShopName,
    string? ShopAddress,
    string? ShopGstin,
    string GeneratedBy,
    DateTimeOffset GeneratedAt,
    DateOnly StartDate,
    DateOnly EndDate,
    string ExportLevel);
