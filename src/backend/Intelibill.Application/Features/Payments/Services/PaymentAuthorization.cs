using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Payments.Services;

internal static class PaymentAuthorization
{
    /// <summary>The actor's role in the shop, or the error that refuses them.</summary>
    public static async Task<ErrorOr<ShopRole>> RoleAsync(
        IUserRepository userRepository,
        Guid actorUserId,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(actorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == shopId);
        if (membership is null || !membership.IsActive)
            return Errors.Shop.MembershipNotFound;

        return membership.Role;
    }

    public static async Task<ErrorOr<Success>> RequireOwnerOrManagerAsync(
        IUserRepository userRepository,
        Guid actorUserId,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var role = await RoleAsync(userRepository, actorUserId, shopId, cancellationToken);
        if (role.IsError)
            return role.Errors;

        return role.Value is ShopRole.Owner or ShopRole.Manager
            ? Result.Success
            : Errors.Payment.UserIsNotOwnerOrManager;
    }

    public static ErrorOr<Success> LockOutcome(RowLockOutcome outcome) => outcome switch
    {
        RowLockOutcome.Acquired => Result.Success,
        RowLockOutcome.NotFound => Errors.Payment.PartyNotFound,
        RowLockOutcome.TimedOut => Errors.Payment.Busy,
        _ => throw new InvalidOperationException($"Unknown payment lock outcome '{outcome}'."),
    };
}
