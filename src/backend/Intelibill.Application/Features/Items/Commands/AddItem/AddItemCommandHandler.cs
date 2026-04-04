using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Commands.AddItem;

public sealed class AddItemCommandHandler(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<ItemDto>> HandleAsync(AddItemCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Item.UserIsNotOwnerOrManager;

        var normalizedBarcode = command.Barcode.Trim();
        var existingBarcode = await itemRepository.GetByBarcodeAsync(command.ActiveShopId, normalizedBarcode, cancellationToken);
        if (existingBarcode is not null)
            return Errors.Item.BarcodeAlreadyExists;

        var normalizedName = command.Name.Trim();
        var existingName = await itemRepository.GetByNameAsync(command.ActiveShopId, normalizedName, cancellationToken);
        if (existingName is not null)
            return Errors.Item.NameAlreadyExists;

        var item = Item.Create(
            command.ActiveShopId,
            normalizedName,
            command.Description,
            command.Uom,
            normalizedBarcode,
            command.IsActive,
            command.PreferredSupplierId,
            command.ActorUserId);

        await itemRepository.AddAsync(item, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ItemDto(
            item.Id,
            item.Name,
            item.Barcode,
            item.Description,
            item.Uom,
            item.IsActive,
            item.PreferredSupplierId);
    }
}