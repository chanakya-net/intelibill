namespace Intelibill.Application.Features.Users.Commands.AcceptShopInvitation;

/// <summary>
/// Redeems an invitation from the invited person's own authenticated account.
/// </summary>
public sealed record AcceptShopInvitationCommand(
    Guid ActorUserId,
    Guid InvitationId,
    string Token);
