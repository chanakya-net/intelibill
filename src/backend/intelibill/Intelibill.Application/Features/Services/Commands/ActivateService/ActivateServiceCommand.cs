using Intelibill.Application.Common.Security;
namespace Intelibill.Application.Features.Services.Commands.ActivateService;

public sealed record ActivateServiceCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid ServiceId,
    SessionClaims? Session);
