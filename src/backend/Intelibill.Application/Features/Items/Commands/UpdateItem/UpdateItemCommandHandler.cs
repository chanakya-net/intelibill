using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Commands.UpdateItem;

public sealed class UpdateItemCommandHandler(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<Success>> HandleAsync(UpdateItemCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Item.UserIsNotOwnerOrManager;

        var item = await itemRepository.GetByIdAsync(command.ItemId, cancellationToken);
        if (item is null)
            return Errors.Item.ItemNotFound;

        if (item.ShopId != command.ActiveShopId)
            return Errors.Item.ItemNotFound;

        var normalizedBarcode = command.Barcode.Trim();
        if (normalizedBarcode != item.Barcode)
        {
            var existingBarcode = await itemRepository.GetByBarcodeAsync(command.ActiveShopId, normalizedBarcode, cancellationToken);
            if (existingBarcode is not null)
                return Errors.Item.BarcodeAlreadyExists;
        }

        item.Update(
            command.Name,
            command.Description,
            command.Uom,
            normalizedBarcode,
            item.IsActive,
            command.ActorUserId);

        itemRepository.Update(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
