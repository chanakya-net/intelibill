namespace Intelibill.Application.Features.Services.Commands.DeactivateService;

public sealed record DeactivateServiceCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid ServiceId);
