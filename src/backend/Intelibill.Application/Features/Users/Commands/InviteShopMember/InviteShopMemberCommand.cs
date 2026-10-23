namespace Intelibill.Application.Features.Users.Commands.InviteShopMember;

/// <summary>
/// Offers a shop-scoped role to one email address. Re-issuing for the same address supersedes the
/// earlier offer, so a resend never leaves two redeemable links.
/// </summary>
public sealed record InviteShopMemberCommand(
    Guid ActorUserId,
    Guid ShopId,
    string Email,
    string Role);
