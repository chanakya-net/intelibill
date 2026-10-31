using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.EditShopUser;

/// <summary>
/// Legacy owner edit of a staff account. It used to rewrite the target's global email, phone,
/// names and login-enabled flag after checking ownership in the active shop only, which let the
/// owner of one shop seize the recovery identity of an account that owns another shop.
/// </summary>
/// <remarks>
/// The payload is now refused explicitly rather than partially applied: an old web client must not
/// be able to keep any part of that control channel. Shop-scoped administration moved to
/// <c>PATCH /api/shops/{shopId}/members/{userId}</c>, onboarding to shop invitations, and sign-in
/// details stay with the account holder's own verified change and recovery flows.
/// </remarks>
public sealed class EditShopUserCommandHandler(IUserRepository userRepository)
{
    public async Task<ErrorOr<ShopUserDto>> HandleAsync(EditShopUserCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null || !actorMembership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role != ShopRole.Owner)
            return Errors.Shop.UserIsNotOwner;

        return Errors.Users.MembershipOperationRequired;
    }
}
