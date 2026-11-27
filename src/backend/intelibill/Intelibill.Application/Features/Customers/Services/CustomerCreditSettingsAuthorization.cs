using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Services;

/// <summary>
/// Credit settings (limit, policy, terms) are an owner's or manager's decision: a staff member who
/// could raise a limit could also step around the override rule.
/// </summary>
internal static class CustomerCreditSettingsAuthorization
{
    /// <summary>Whether the actor may set credit settings. A null actor is a trusted internal caller.</summary>
    public static async Task<ErrorOr<bool>> CanChangeAsync(
        IUserRepository userRepository,
        Guid? actorUserId,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        if (actorUserId is not { } userId)
            return true;

        var actor = await userRepository.GetByIdWithDetailsAsync(userId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(m => m.ShopId == shopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        return membership.Role is ShopRole.Owner or ShopRole.Manager;
    }
}
