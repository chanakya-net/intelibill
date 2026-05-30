using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Events;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Wolverine;

namespace Intelibill.Application.Features.Items.Commands.UpdateItem;

public sealed class UpdateItemCommandHandler(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IUnitOfWork unitOfWork,
    IMessageBus messageBus)
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

        var normalizedName = command.Name.Trim();
        var normalizedBarcode = command.Barcode.Trim();
        var normalizedUom = command.Uom.Trim();
        var targetIsActive = command.IsActive ?? item.IsActive;
        var isRenamed = normalizedName != item.Name;
        var isDeactivated = item.IsActive && !targetIsActive;

        if (normalizedBarcode != item.Barcode)
        {
            var existingBarcode = await itemRepository.GetByBarcodeAsync(command.ActiveShopId, normalizedBarcode, cancellationToken);
            if (existingBarcode is not null)
                return Errors.Item.BarcodeAlreadyExists;
        }

        item.Update(
            normalizedName,
            command.Description,
            normalizedUom,
            normalizedBarcode,
            targetIsActive,
            command.ActorUserId,
            command.HsnCode,
            command.DefaultTaxRatePercent,
            defaultTaxIncluded: false);

        itemRepository.Update(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (isRenamed || isDeactivated)
        {
            await messageBus.PublishAsync(
                new ItemApplicabilityChangedDomainEvent(
                    command.ActiveShopId,
                    item.Id));
        }

        return Result.Success;
    }
}
