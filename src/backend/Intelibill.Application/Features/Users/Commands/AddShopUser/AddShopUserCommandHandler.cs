using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Extensions;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.AddShopUser;

public sealed class AddShopUserCommandHandler(
    IValidator<AddShopUserCommand> validator,
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<ShopUserDto>> HandleAsync(AddShopUserCommand command, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is { IsError: true } err) return err.Errors;

        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role != ShopRole.Owner)
            return Errors.Shop.UserIsNotOwner;

        if (!TryParseShopRole(command.Role, out var role))
            return Errors.Users.RoleNotSupported;

        var normalizedPhone = command.PhoneNumber.Trim();
        if (await userRepository.ExistsByPhoneAsync(normalizedPhone, cancellationToken))
            return Errors.Auth.PhoneAlreadyInUse;

        var shop = actorMembership.Shop ?? await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var passwordHash = passwordHasher.Hash(command.Password);
        var newUser = User.CreateWithPhone(normalizedPhone, command.FirstName, command.LastName);
        newUser.UpdatePassword(passwordHash);

        var membership = ShopMembership.Create(shop.Id, newUser.Id, role, false);
        shop.AddMembership(membership);
        newUser.AddShopMembership(membership);

        await userRepository.AddAsync(newUser, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ShopUserDto(
            newUser.Id,
            newUser.FirstName,
            newUser.LastName,
            newUser.Email,
            newUser.PhoneNumber,
            ToRoleLabel(role));
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
