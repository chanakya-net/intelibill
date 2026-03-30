using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Extensions;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.EditShopUser;

public sealed class EditShopUserCommandHandler(
    IValidator<EditShopUserCommand> validator,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<ShopUserDto>> HandleAsync(EditShopUserCommand command, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is { IsError: true } err) return err.Errors;

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
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ShopUserDto(
            targetUser.Id,
            targetUser.FirstName,
            targetUser.LastName,
            targetUser.Email,
            targetUser.PhoneNumber,
            ToRoleLabel(role),
            targetUser.IsLoginEnabled);
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
        return role == ShopRole.Staff ? "SalesPerson" : role.ToString();
    }
}
