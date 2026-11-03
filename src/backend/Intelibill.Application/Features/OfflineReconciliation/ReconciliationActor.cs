using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.OfflineReconciliation;

/// <summary>The caller's live authority in the shop, read on every request rather than trusted from the token.</summary>
internal static class ReconciliationActor
{
    /// <summary>The caller's role through an active membership, or <c>Shop.MembershipNotFound</c>.</summary>
    public static async Task<ErrorOr<ShopRole>> ResolveRoleAsync(
        IUserRepository userRepository,
        Guid actorUserId,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        // Only active memberships are loaded, so a suspended or removed member is refused here.
        var actor = await userRepository.GetByIdWithDetailsAsync(actorUserId, cancellationToken);
        var membership = actor?.ShopMemberships.FirstOrDefault(m => m.ShopId == shopId && m.IsActive);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        return membership.Role;
    }

    /// <summary>Staff see only the cases they filed; everyone else in the shop sees all of them.</summary>
    public static Guid? VisibleCreator(ShopRole role, Guid actorUserId) =>
        role is ShopRole.Owner or ShopRole.Manager ? null : actorUserId;
}
