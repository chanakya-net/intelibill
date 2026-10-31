namespace Intelibill.Application.Features.Users.Commands.RevokeShopInvitation;

public sealed record RevokeShopInvitationCommand(Guid ActorUserId, Guid ShopId, Guid InvitationId);
