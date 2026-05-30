using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.EditShopUser;

public sealed class EditShopUserCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<ShopUserDto>> HandleAsync(EditShopUserCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role != ShopRole.Owner)
            return Errors.Shop.UserIsNotOwner;

        var targetUser = await userRepository.GetByIdWithDetailsAsync(command.TargetUserId, cancellationToken);
        if (targetUser is null)
            return Errors.Auth.UserNotFound;

        var targetMembership = targetUser.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (targetMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (targetMembership.Role == ShopRole.Owner)
            return Errors.Users.CannotModifyOwner;

        if (!TryParseShopRole(command.Role, out var role))
            return Errors.Users.RoleNotSupported;

        var normalizedEmail = command.Email.Trim().ToLowerInvariant();
        var emailChanged = !string.Equals(targetUser.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase);
        if (emailChanged && await userRepository.ExistsByEmailAsync(normalizedEmail, cancellationToken))
            return Errors.Auth.EmailAlreadyInUse;

        var normalizedPhone = command.PhoneNumber.Trim();
        if (await userRepository.ExistsByPhoneAsync(normalizedPhone, targetUser.Id, cancellationToken))
            return Errors.Auth.PhoneAlreadyInUse;

        targetUser.UpdateShopUserProfile(normalizedEmail, command.FirstName, command.LastName, normalizedPhone);
        targetUser.SetLoginEnabled(command.IsLoginEnabled);
        targetMembership.SetRole(role);

        userRepository.Update(targetUser);

        IReadOnlyList<Guid> finalShopIds;

        if (command.ShopIds is not null)
        {
            var actorOwnedShopIds = actor.ShopMemberships
                .Where(sm => sm.Role == ShopRole.Owner)
                .Select(sm => sm.ShopId)
                .ToHashSet();

            var requestedShopIds = command.ShopIds
                .Where(id => actorOwnedShopIds.Contains(id))
                .ToHashSet();

            var currentMemberships = await shopRepository.GetMembershipsForUsersInShopsAsync(
                [command.TargetUserId],
                actorOwnedShopIds.ToList(),
                cancellationToken);

            foreach (var membership in currentMemberships)
            {
                if (!requestedShopIds.Contains(membership.ShopId))
                    shopRepository.RemoveMembership(membership);
            }

            var currentShopIds = currentMemberships.Select(sm => sm.ShopId).ToHashSet();

            foreach (var shopId in requestedShopIds)
            {
                if (!currentShopIds.Contains(shopId))
                {
                    var newMembership = ShopMembership.Create(shopId, command.TargetUserId, role, isDefault: false);
                    await shopRepository.AddMembershipAsync(newMembership, cancellationToken);
                }
            }

            finalShopIds = requestedShopIds.ToList();
        }
        else
        {
            finalShopIds = targetUser.ShopMemberships.Select(sm => sm.ShopId).ToList();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ShopUserDto(
            targetUser.Id,
            targetUser.FirstName,
            targetUser.LastName,
            targetUser.Email,
            targetUser.PhoneNumber,
            ToRoleLabel(role),
            targetUser.IsLoginEnabled,
            finalShopIds,
            targetUser.Language);
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

    private static string ToRoleLabel(ShopRole role)
    {
        return role == ShopRole.Staff ? "Staff" : role.ToString();
    }
}
