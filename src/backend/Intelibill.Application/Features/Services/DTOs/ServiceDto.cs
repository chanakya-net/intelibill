namespace Intelibill.Application.Features.Services.DTOs;

public sealed record ServiceDto(
    Guid ServiceId,
    string Code,
    string Name,
    string? Description,
    decimal Price,
    string? HsnCode,
    decimal TaxRatePercent,
    bool TaxIncluded,
    bool IsActive);
