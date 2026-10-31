namespace Intelibill.Application.Features.Users.Commands.UpdateShopMembership;

/// <summary>
/// Changes one membership's shop-scoped role and status. <paramref name="ExpectedVersion"/> is the
/// authority version the caller read, so a stale screen cannot overwrite a newer decision.
/// </summary>
public sealed record UpdateShopMembershipCommand(
    Guid ActorUserId,
    Guid ShopId,
    Guid TargetUserId,
    string Role,
    string Status,
    long ExpectedVersion);
