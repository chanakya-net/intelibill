using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Extensions;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Commands.UpdateShop;

public sealed class UpdateShopCommandHandler(
    IValidator<UpdateShopCommand> validator,
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<ShopDetailsDto>> HandleAsync(UpdateShopCommand command, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is { IsError: true } err) return err.Errors;

        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;

        var membership = user.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role != ShopRole.Owner)
            return Errors.Shop.UserIsNotOwner;

        var shop = membership.Shop ?? await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        shop.UpdateDetails(
            command.Name,
            command.Address,
            command.City,
            command.State,
            command.Pincode,
            command.ContactPerson,
            command.MobileNumber);

        shopRepository.Update(shop);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ShopDetailsDto(
            shop.Id,
            shop.Name,
            shop.Address,
            shop.City,
            shop.State,
            shop.Pincode,
            shop.ContactPerson,
            shop.MobileNumber);
    }
}
