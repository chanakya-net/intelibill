namespace Intelibill.Application.Features.Services.Commands.AddService;

public sealed record AddServiceCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    string Name,
    string? Description,
    decimal Price,
    string? HsnCode,
    decimal TaxRatePercent,
    bool TaxIncluded,
    bool IsActive);
