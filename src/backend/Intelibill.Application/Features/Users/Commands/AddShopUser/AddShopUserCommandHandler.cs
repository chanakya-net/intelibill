using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.AddShopUser;

/// <summary>
/// Legacy owner-creates-staff flow. It minted a global account with an owner-selected password,
/// which made those credentials a permanent cross-tenant control channel, and it refused existing
/// SaaS identities outright.
/// </summary>
/// <remarks>
/// Onboarding now runs through <c>POST /api/shops/{shopId}/invitations</c>: the invited person
/// accepts from their own verified account, so the shop gets a membership and never the identity.
/// </remarks>
public sealed class AddShopUserCommandHandler(IUserRepository userRepository)
{
    public async Task<ErrorOr<ShopUserDto>> HandleAsync(AddShopUserCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembershipsByShop = actor.ShopMemberships.ToDictionary(sm => sm.ShopId, sm => sm);

        foreach (var shopId in command.ShopIds)
        {
            if (!actorMembershipsByShop.TryGetValue(shopId, out var actorMembership) || !actorMembership.IsActive)
                return Errors.Shop.MembershipNotFound;

            if (actorMembership.Role != ShopRole.Owner)
                return Errors.Shop.UserIsNotOwner;
        }

        if (!MembershipAuthority.TryParseAssignableRole(command.Role, out _))
            return Errors.Users.RoleNotSupported;

        return Error.Conflict(
            "Auth.InvitationRequired",
            "Invite this person instead: they accept from their own verified account, and the shop receives a membership rather than control of their sign-in details.");
    }
}
