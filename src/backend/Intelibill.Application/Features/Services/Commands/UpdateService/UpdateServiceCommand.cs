namespace Intelibill.Application.Features.Services.Commands.UpdateService;

public sealed record UpdateServiceCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid ServiceId,
    string Name,
    string? Description,
    decimal Price,
    string? HsnCode,
    decimal TaxRatePercent,
    bool TaxIncluded);
