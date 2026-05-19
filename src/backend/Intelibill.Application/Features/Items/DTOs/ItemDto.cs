namespace Intelibill.Application.Features.Items.DTOs;

public sealed record ItemDto(
    Guid Id,
    string Name,
    string Barcode,
    string? Description,
    string Uom,
    bool IsActive,
    decimal CurrentStock,
    string? HsnCode,
    decimal DefaultTaxRatePercent,
    bool DefaultTaxIncluded);
