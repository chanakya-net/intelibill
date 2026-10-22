using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.AddShopUser;

public sealed class AddShopUserCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<ShopUserDto>> HandleAsync(AddShopUserCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembershipsByShop = actor.ShopMemberships
            .ToDictionary(sm => sm.ShopId, sm => sm);

        foreach (var shopId in command.ShopIds)
        {
            if (!actorMembershipsByShop.TryGetValue(shopId, out var actorMembership))
                return Errors.Shop.MembershipNotFound;

            if (actorMembership.Role != ShopRole.Owner)
                return Errors.Shop.UserIsNotOwner;
        }

        if (!TryParseShopRole(command.Role, out var role))
            return Errors.Users.RoleNotSupported;

        _ = (shopRepository, passwordHasher, unitOfWork, role);
        return Error.Conflict("Auth.InvitationRequired", "Staff must accept an invitation and verify their own identity. Direct password-based staff creation is unavailable until invitation onboarding is enabled.");
    }
    private static bool TryParseShopRole(string roleValue, out ShopRole role)
    {
        var normalizedRole = roleValue.Trim().Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();

        if (normalizedRole == "manager")
        {
            role = ShopRole.Manager;
            return true;
        }

        if (normalizedRole is "salesperson" or "staff")
        {
            role = ShopRole.Staff;
            return true;
        }

        role = default;
        return false;
    }

}
